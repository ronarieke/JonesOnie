"""Build one reward-labelled basis model per instrument from SQL and upsert to Cosmos DB."""
from __future__ import annotations
import argparse, hashlib, json, logging, os
from datetime import datetime, timezone
from typing import Any
import numpy as np
import pyodbc
from azure.cosmos import CosmosClient

logging.basicConfig(level=os.getenv("LOG_LEVEL", "INFO"), format="%(asctime)s %(levelname)s %(message)s")
LOG = logging.getLogger("basis-builder")
MAX_BASIS_SIZE = 32
SQL_QUERY = """
SELECT TOP (?) Id, InstrumentName, FeatureJson, Reward
FROM dbo.TradeDecisions WHERE FeatureSChemaVersion = 'KNN-MonteCarlo-RL-trade-gate-v1'
AND InstrumentName = ?
ORDER BY DecisionTimeUtc DESC;
"""
#WHERE ABS(Reward) > 0 AND Reward IS NOT NULL

def vector_from_json(
    raw: str,
    feature_names: list[str] | None
) -> tuple[np.ndarray, list[str]]:

    obj = json.loads(raw)

    if not isinstance(obj, dict):
        raise ValueError("FeatureJson must be a JSON object")

    excluded_features = {
        "Instrument",
        "InstrumentName",
        "ModelVersion",
        "FeatureSchemaVersion",
        "Symbol",
        "TradeDecisionId",
        "AccountId",
        "CreatedUtc",
        "UpdatedUtc"
    }

    numeric: dict[str, float] = {}

    for key, value in obj.items():

        if key in excluded_features:
            continue

        # Treat missing numeric features as zero in Q.
        if value is None:
            numeric[key] = 0.0
            continue
        
        # bool is a subclass of int, so handle it explicitly.
        if isinstance(value, bool):
            numeric[key] = 1.0 if value else 0.0
            continue

        if isinstance(value, (int, float)):
            numeric[key] = float(value)
            continue

        # Optionally accept numeric strings while ignoring metadata strings.
        if isinstance(value, str):
            try:
                numeric[key] = float(value)
            except ValueError:
                continue

    if not numeric:
        raise ValueError("FeatureJson contains no numeric features")

    # The first valid row establishes the ordered feature schema.
    if feature_names is None:
        names = sorted(numeric.keys())
    else:
        names = feature_names

    # Features missing from later schema versions are imputed as zero.
    vector = np.asarray(
        [numeric.get(name, 0.0) for name in names],
        dtype=np.float64
    )

    if not np.all(np.isfinite(vector)):
        raise ValueError("Feature vector contains NaN or infinity")

    if np.linalg.norm(vector, ord=1) == 0:
        raise ValueError("Feature vector contains only zeros")

    return vector, names

def build(instrument: str, candidate_multiplier: int = 8, rcond: float = 1e-10) -> dict[str, Any]:
    with pyodbc.connect(os.environ["SQL_CONNECTION_STRING"]) as cn:
        rows = cn.cursor().execute(SQL_QUERY, candidate_multiplier * 1024, instrument).fetchall()
    parsed, feature_names = [], None
    for row in rows:
        try:
            reward = (
                float(row.Reward)
                if row.Reward is not None
                else 0.0
            )
            v, feature_names = vector_from_json(row.FeatureJson, feature_names)
            parsed.append((str(row.Id), v, float(reward)))
        except (ValueError, TypeError, json.JSONDecodeError) as exc:
            LOG.warning("Skipping decision %s: %s", row.Id, exc)
    
    print(f"Fetched {len(rows)} rows for {instrument}")
    if not parsed:
        LOG.warning(
            "%s has no usable rows. Skipping.",
            instrument
        )
        return None    
    if not parsed: raise RuntimeError(f"No usable rows for {instrument}")
    d = len(feature_names)
    basis_size = min(
        len(parsed),
        len(feature_names),
        MAX_BASIS_SIZE
    )
    # Keep the newest d rows when they span the feature space; otherwise greedily add rank-increasing rows.
    selected, current_rank = [], 0
    for item in parsed:
        candidate = np.column_stack([x[1] for x in selected + [item]])
        rank = int(np.linalg.matrix_rank(candidate))
        if rank > current_rank:
            selected.append(item); current_rank = rank
        if len(selected) == basis_size: break
    if len(selected) < basis_size:
        raise RuntimeError(f"Need {basis_size} independent rows; found rank {current_rank} from {len(parsed)} usable rows")
    B = np.column_stack([x[1] for x in selected])  # each historical state is one basis column
    B_plus = np.linalg.pinv(B, rcond=rcond)
    singular = np.linalg.svd(B, compute_uv=False)
    schema_hash = hashlib.sha256("\n".join(feature_names).encode()).hexdigest()
    now = datetime.now(timezone.utc).isoformat()
    return {
        "id": instrument, "instrument": instrument, "modelVersion": now,
        "featureNames": feature_names, "featureSchemaHash": schema_hash,
        "basisDecisionIds": [x[0] for x in selected],
        "basisRewards": [x[2] for x in selected],
        "basis": B.tolist(), "pseudoInverse": B_plus.tolist(),
        "rank": int(np.linalg.matrix_rank(B)), "singularValues": singular.tolist(),
        "conditionNumber": float(np.linalg.cond(B)), "rcond": rcond, "generatedAtUtc": now,
    }

def store(doc: dict[str, Any]) -> None:
    client = CosmosClient.from_connection_string(os.environ["COSMOS_CONNECTION_STRING"])
    container = client.get_database_client(os.environ["COSMOS_DATABASE"]).get_container_client(os.environ["COSMOS_CONTAINER"])
    container.upsert_item(doc)
    LOG.info("Stored basis for %s, rank=%s, condition=%g", doc["instrument"], doc["rank"], doc["conditionNumber"])

def main() -> None:
    p=argparse.ArgumentParser(); p.add_argument("--instrument", required=True); p.add_argument("--candidate-multiplier", type=int, default=8); p.add_argument("--rcond", type=float, default=1e-10)

    a = p.parse_args()

    doc = build(
        a.instrument,
        a.candidate_multiplier,
        a.rcond
    )

    if doc is None:
        LOG.warning(
            "No basis created for %s",
            a.instrument
        )
        return

    store(doc)
if __name__ == "__main__": main()
