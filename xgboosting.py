import pandas as pd
import numpy as np

df = pd.read_csv("training.csv", header=None)

# Convert invalid numeric text and infinities to NaN
df = df.apply(pd.to_numeric, errors="coerce")
df = df.replace([np.inf, -np.inf], np.nan)

print("Missing values before cleanup:", df.isna().sum().sum())

# Remove rows with any missing feature or target
df = df.dropna().reset_index(drop=True)

X = df.iloc[:, :-1].astype(np.float32)
y = df.iloc[:, -1].astype(np.float32)

print("Rows after cleanup:", len(df))
print("X shape:", X.shape)
print("y shape:", y.shape)


train_size = int(len(df) * 0.6)
valid_size = int(len(df) * 0.2)

X_train = X[:train_size]
y_train = y[:train_size]

X_valid = X[train_size:train_size+valid_size]
y_valid = y[train_size:train_size+valid_size]

X_test = X[train_size+valid_size:]
y_test = y[train_size+valid_size:]

from xgboost import XGBRegressor

# model = XGBRegressor(
#     n_estimators=1000,
#     max_depth=16,
#     learning_rate=0.02,
#     objective='reg:squarederror'
# )


model = XGBRegressor(
    n_estimators=500,
    max_depth=8,
    learning_rate=0.03,
    subsample=0.8,
    colsample_bytree=0.8,
    objective='reg:squarederror'
)

model.fit(
    X_train,
    y_train
)


pred = model.predict(X_test)

test = pd.DataFrame(
{
    "pred": pred,
    "actual": y_test
})

test = test.sort_values(
    "pred",
    ascending=False)

top10 = test.head(
    int(len(test) * 0.10))

print(
    top10.actual.mean())


model.save_model(
    "reward_model.json")


import onnxmltools
from onnxmltools.convert.common.data_types import FloatTensorType

initial_type = [
    ("float_input",
     FloatTensorType([None, X.shape[1]]))
]

onnx_model = onnxmltools.convert_xgboost(
    model,
    initial_types=initial_type
)

with open("reward_model.onnx", "wb") as f:
    f.write(onnx_model.SerializeToString())


print(df.iloc[:, -1].describe())

print(y.min())
print(y.max())
print(y.mean())

print("Rows:", len(df))
print("Reward min:", y.min())
print("Reward max:", y.max())
print("Reward mean:", y.mean())
print("Reward std:", y.std())

from sklearn.metrics import (
    mean_absolute_error,
    mean_squared_error,
    r2_score
)

mae = mean_absolute_error(y_test, pred)
rmse = np.sqrt(mean_squared_error(y_test, pred))
r2 = r2_score(y_test, pred)

print("MAE :", mae)
print("RMSE:", rmse)
print("R²  :", r2)

pred = model.predict(X_test)

eval_df = pd.DataFrame({
    "Actual": y_test,
    "Predicted": pred
})

eval_df = eval_df.sort_values(
    "Predicted",
    ascending=False
)

top10 = eval_df.head(int(len(eval_df)*0.10))
bottom10 = eval_df.tail(int(len(eval_df)*0.10))

print("Top 10% avg reward:", top10["Actual"].mean())
print("Bottom 10% avg reward:", bottom10["Actual"].mean())
for p in [50,60,70,80,90,95]:
    keep = pred >= np.percentile(pred, p)

    print(
        p,
        y_test[keep].mean(),
        keep.sum()
    )

print(y.nsmallest(20))
print(y.describe(percentiles=[
    .001,.01,.05,.1,.9,.95,.99,.999
]))

print(y.idxmin())
print(X.loc[y.idxmin()])
print(y.loc[y.idxmin()])
importance = model.feature_importances_

for idx, score in sorted(
    enumerate(importance),
    key=lambda x: x[1],
    reverse=True
)[:20]:
    print(idx, score)