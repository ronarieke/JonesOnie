using FibonacciTrade.Algorithm.MonteCarlo.Objects;
using FibonacciTrade.ALgorithm.MonteCarlo.Objects;
using FibonacciTrade.Data.Algorithm.Objects;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace FibonacciTrade.Algorithm.MonteCarlo;
public interface IMonteCarloStore
{
    Task<MonteCarloPredictionDocument?> GetAsync(
        string symbol,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        MonteCarloPredictionDocument document,
        CancellationToken cancellationToken = default);
}
public interface IMonteCarloPredictionService
{
    Task<MonteCarloPredictionDocument> GenerateAsync(
ConsensusTradeCandidate trade,
int simulations = 5000,
        int steps = 10,
        CancellationToken cancellationToken = default);

    Task<MonteCarloPredictionDocument> GetOrCreateAsync(
ConsensusTradeCandidate trade, int simulations = 5000,
        int steps = 10,
        CancellationToken cancellationToken = default);
}


public sealed class MonteCarloPredictionService
    : IMonteCarloPredictionService
{
    private readonly MonteCarloPredictor _predictor;
    private readonly IMonteCarloStore _store;
    private readonly ILogger<MonteCarloPredictionService> _logger;

    public MonteCarloPredictionService(
        MonteCarloPredictor predictor,
        IMonteCarloStore store,
        ILogger<MonteCarloPredictionService> logger = null)
    {
        ArgumentNullException.ThrowIfNull(predictor);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);

        _predictor = predictor;
        _store = store;
        _logger = logger;
    }

    public async Task<MonteCarloPredictionDocument> GetOrCreateAsync(
        ConsensusTradeCandidate trade,
        int simulations = 5000,
        int steps = 10,
        CancellationToken cancellationToken = default)
    {
        var symbol = NormalizeSymbol(trade.InstrumentName);

        MonteCarloPredictionDocument? existing =
            await _store.GetAsync(
                symbol,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        _logger.LogInformation(
            "Monte Carlo prediction not found for {Symbol}. " +
            "Generating a new prediction.",
            symbol);

        return await GenerateAsync(
            trade,
            simulations,
            steps,
            cancellationToken);
    }

    public async Task<MonteCarloPredictionDocument> GenerateAsync(
        ConsensusTradeCandidate trade,
        int simulations = 5000,
        int steps = 10,
        CancellationToken cancellationToken = default)
    {
        var symbol = NormalizeSymbol(trade.InstrumentName);

        _logger.LogInformation(
            "Generating Monte Carlo prediction for {Symbol} " +
            "using {SimulationCount} simulations and {StepCount} steps.",
            symbol,
            simulations,
            steps);

        MonteCarloPrediction prediction =
            await _predictor.PredictAsync(
                trade,
                simulations,
                steps,
                cancellationToken);

        MonteCarloPredictionDocument document =
            CreateDocument(prediction);

        await _store.UpsertAsync(
            document,
            cancellationToken);

        _logger.LogInformation(
            "Stored Monte Carlo prediction for {Symbol}. " +
            "Mean return: {MeanReturn}; probability of profit: {ProbProfit}.",
            symbol,
            document.MeanReturn,
            document.ProbProfit);

        return document;
    }

    private static MonteCarloPredictionDocument CreateDocument(
        MonteCarloPrediction prediction)
    {
        ArgumentNullException.ThrowIfNull(prediction);

        string symbol = NormalizeSymbol(prediction.Symbol);

        return new MonteCarloPredictionDocument
        {
            id = $"{symbol}_MONTECARLO",
            Symbol = symbol,
            DocumentType = "MONTECARLO",
            CreatedUtc = prediction.CalculatedUtc,

            MeanReturn = prediction.MeanReturn,
            StdDev = prediction.StdDev,
            ProbProfit = prediction.ProbProfit,
            WorstDecile = prediction.WorstDecile,
            MedianReturn = prediction.MedianReturn,
            BestDecile = prediction.BestDecile,
            RiskAdjustedReturn = prediction.RiskAdjustedReturn,
            SimulationCount = prediction.SimulationCount,
            Steps = prediction.Steps
        };
    }

    private static string NormalizeSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException(
                "Symbol cannot be empty.",
                nameof(symbol));
        }

        return symbol
            .Trim()
            .ToUpperInvariant();
    }
}
public sealed class MonteCarloPredictor
{
    private readonly Container _projectionsContainer;
    private readonly Random _random;

    public MonteCarloPredictor(
        CosmosClient cosmosClient,
        int? randomSeed = null)
    {
        ArgumentNullException.ThrowIfNull(cosmosClient);

        _projectionsContainer = cosmosClient.GetContainer(
            databaseId: "oanda",
            containerId: "projections");

        _random = randomSeed.HasValue
            ? new Random(randomSeed.Value)
            : Random.Shared;
    }
    private const int USD = 0;
    private const int EUR = 1;
    private const int GBP = 2;
    private const int JPY = 3;
    private const int AUD = 4;
    private const int NZD = 5;
    private const int CAD = 6;
    private const int CHF = 7;
    private const int GLOBAL = 8;
    private static void ApplyCurrency(
    double[] beta,
    string currency,
    double value)
    {
        switch (currency.ToUpperInvariant())
        {
            case "USD":
                beta[USD] += value;
                break;

            case "EUR":
                beta[EUR] += value;
                break;

            case "GBP":
                beta[GBP] += value;
                break;

            case "JPY":
                beta[JPY] += value;
                break;

            case "AUD":
                beta[AUD] += value;
                break;

            case "NZD":
                beta[NZD] += value;
                break;

            case "CAD":
                beta[CAD] += value;
                break;

            case "CHF":
                beta[CHF] += value;
                break;
        }
    }
    private MarketState BuildMarketState(
        ConsensusTradeCandidate trade)
    {
        var h4 = trade.ByGranularity["H4"];
        var h8 = trade.ByGranularity["H8"];
        var d = trade.ByGranularity["D"];

        return new MarketState
        {
            Symbol = trade.InstrumentName,

            ExpectedReturn =
                (double)((h4.Forward +
                          h8.Forward +
                          d.Forward) / 3m),

            Volatility =
                (double)((h4.StdDev +
                          h8.StdDev +
                          d.StdDev) / 3m),

            Confidence =
                (double)trade.AverageConfidence,

            Betas = BuildBetas(trade.InstrumentName),

            CurrentReturn = 0
        };
    }
    private double[] BuildBetas(
    string instrument)
    {
        string[] parts =
            instrument.Split('_');

        string baseCurrency = parts[0];
        string quoteCurrency = parts[1];

        double[] beta = new double[9];

        ApplyCurrency(
            beta,
            baseCurrency,
            +1.0);

        ApplyCurrency(
            beta,
            quoteCurrency,
            -1.0);

        beta[8] = 1.0; // global risk factor

        return beta;
    }
    private double[] BuildTheta()
    {
        return new double[]
        {
        GenerateShock(0.30), // USD
        GenerateShock(0.22), // GBP
        GenerateShock(0.18), // AUD
        GenerateShock(0.10), // NZD
        GenerateShock(0.15), // EUR
        GenerateShock(0.15), // JPY
        GenerateShock(0.10), // CAD
        GenerateShock(0.10), // CHF
        GenerateShock(0.12)  // Global
        };
    }

    private double GenerateShock(double probability)
    {
        if (_random.NextDouble() > probability)
            return 0;

        return NextGaussian();
    }
    public async Task<MonteCarloPrediction> PredictAsync(
    ConsensusTradeCandidate trade,
    int simulations = 5000,
    int steps = 10,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trade.InstrumentName))
        {
            throw new ArgumentException(
                "Symbol cannot be empty.",
                nameof(trade.InstrumentName));
        }

        ValidateSimulationArguments(simulations, steps);

        ConsensusProjection consensus =
            await GetLatestConsensusAsync(cancellationToken);

        //ConsensusTradeCandidate? trade = consensus.Trades
        //    .FirstOrDefault(x =>
        //        string.Equals(
        //            x.InstrumentName,
        //            symbol,
        //            StringComparison.OrdinalIgnoreCase));

        if (trade is null)
        {
            throw new InvalidOperationException(
                $"The latest consensus does not contain {trade.InstrumentName}.");
        }

        MarketState state = BuildMarketState(trade);

        return Predict(
            state,
            BuildTheta(),
            simulations,
            steps);
    }
    public async Task<IReadOnlyList<MonteCarloPrediction>> PredictAllAsync(
    int simulations = 5000,
    int steps = 10,
    CancellationToken cancellationToken = default)
    {
        ValidateSimulationArguments(simulations, steps);

        ConsensusProjection consensus =
            await GetLatestConsensusAsync(cancellationToken);

        var predictions =
            new List<MonteCarloPrediction>(consensus.Trades.Count);

        foreach (ConsensusTradeCandidate trade in consensus.Trades)
        {
            MarketState state = BuildMarketState(trade);

            MonteCarloPrediction prediction = Predict(
                state,
                BuildTheta(),
                simulations,
                steps);

            predictions.Add(prediction);
        }

        return predictions;
    }

    private MonteCarloPrediction Predict(
    MarketState state,
    double[] theta,
    int simulations,
    int steps)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(theta);

        ValidateSimulationArguments(simulations, steps);

        if (string.IsNullOrWhiteSpace(state.Symbol))
        {
            throw new ArgumentException(
                "Market-state symbol cannot be empty.",
                nameof(state));
        }

        if (state.Volatility < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "Market-state volatility cannot be negative.");
        }

        if (state.Betas == null || state.Betas.Length == 0)
        {
            throw new ArgumentException(
                "Market-state beta values cannot be empty.",
                nameof(state));
        }

        if (state.Betas.Length != theta.Length)
        {
            throw new ArgumentException(
                $"Beta vector length ({state.Betas.Length}) must match " +
                $"theta vector length ({theta.Length}).",
                nameof(theta));
        }

        /*
         * For a 12-hour projection with 10 steps:
         *
         * alpha = 0.1
         * each step represents 1.2 hours
         */
        double alpha = 1.0 / steps;
        double sqrtAlpha = Math.Sqrt(alpha);

        /*
         * beta(i)' * theta(t)
         *
         * For GBP_USD:
         *     betaGBP * thetaGBP
         *   - betaUSD * thetaUSD
         *   + betaAll * thetaAll
         */
        double factorAdjustment =
            DotProduct(state.Betas, theta);

        double adjustedExpectedReturn =
            state.ExpectedReturn;

        double[] simulatedReturns =
            new double[simulations];

        for (int simulation = 0;
             simulation < simulations;
             simulation++)
        {
            /*
             * Start from the current accumulated return, if supplied.
             * Otherwise CurrentReturn should default to zero.
             */
            double projectedReturn =
                state.CurrentReturn;

            /*
             * This shock remains constant throughout this particular
             * simulated path, creating a coherent short-term regime.
             */
            double sharedMarketShock =
                NextGaussian();

            for (int step = 0; step < steps; step++)
            {
                double residualShock =
                    NextGaussian();

                double driftIncrement =
                    adjustedExpectedReturn * alpha;

                /*
                 * The factor adjustment determines sensitivity to the
                 * shared market shock.
                 */
                double sharedShockIncrement =
    state.Volatility *
    factorAdjustment *
    sharedMarketShock *
    sqrtAlpha;

                double residualShockIncrement =
                    state.Volatility *
                    residualShock *
                    sqrtAlpha;

                projectedReturn +=
                    driftIncrement +
                    sharedShockIncrement +
                    residualShockIncrement;
            }

            simulatedReturns[simulation] =
                projectedReturn;
        }

        return CalculateStatistics(
            state.Symbol,
            simulatedReturns,
            simulations,
            steps);
    }
    private static double DotProduct(
    double[] a,
    double[] b)
    {
        if (a == null)
            throw new ArgumentNullException(nameof(a));

        if (b == null)
            throw new ArgumentNullException(nameof(b));

        if (a.Length != b.Length)
            throw new ArgumentException(
                "Vectors must have the same length.");

        double sum = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }
    private async Task<ConsensusProjection> GetLatestConsensusAsync(
    CancellationToken cancellationToken)
    {
        ItemResponse<ConsensusProjection> response =
            await _projectionsContainer.ReadItemAsync<ConsensusProjection>(
                "LATEST_CONSENSUS",
                new PartitionKey("LATEST"),
                cancellationToken: cancellationToken);

        return response.Resource;
    }
    private async Task<IReadOnlyList<ProjectionDocument>>
        GetLatestProjectionsAsync(
            CancellationToken cancellationToken)
    {
        const string queryText = """
            SELECT *
            FROM p
            ORDER BY p.createdUtc DESC
            """;

        using FeedIterator<ProjectionDocument> iterator =
            _projectionsContainer.GetItemQueryIterator<ProjectionDocument>(
                new QueryDefinition(queryText));

        var documents = new List<ProjectionDocument>();

        while (iterator.HasMoreResults)
        {
            FeedResponse<ProjectionDocument> response =
                await iterator.ReadNextAsync(cancellationToken);

            documents.AddRange(response);
        }

        /*
         * Keep the newest document for each symbol.
         *
         * The Cosmos query returns newest documents first, but we still
         * explicitly order each group so correctness does not depend on
         * response-page ordering.
         */
        return documents
            .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
            .GroupBy(
                x => x.Symbol,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(x => x.CreatedUtc)
                .First())
            .OrderBy(x => x.Symbol)
            .ToList();
    }

    private static MonteCarloPrediction CalculateStatistics(
        string symbol,
        double[] simulatedReturns,
        int simulations,
        int steps)
    {
        if (simulatedReturns.Length == 0)
        {
            throw new ArgumentException(
                "At least one simulated return is required.",
                nameof(simulatedReturns));
        }

        Array.Sort(simulatedReturns);

        double mean = simulatedReturns.Average();

        double variance = simulatedReturns
            .Select(x => Math.Pow(x - mean, 2))
            .Average();

        double stdDev = Math.Sqrt(variance);

        double probProfit =
            simulatedReturns.Count(x => x > 0)
            / (double)simulatedReturns.Length;

        double worstDecile = Percentile(simulatedReturns, 0.10);
        double medianReturn = Percentile(simulatedReturns, 0.50);
        double bestDecile = Percentile(simulatedReturns, 0.90);

        return new MonteCarloPrediction
        {
            Symbol = symbol,
            MeanReturn = mean,
            StdDev = stdDev,
            ProbProfit = probProfit,
            WorstDecile = worstDecile,
            BestDecile = bestDecile,
            MedianReturn = medianReturn,
            SimulationCount = simulations,
            Steps = steps,
            CalculatedUtc = DateTime.UtcNow
        };
    }

    private static double Percentile(
        IReadOnlyList<double> sortedValues,
        double percentile)
    {
        if (sortedValues.Count == 0)
        {
            throw new ArgumentException(
                "The collection cannot be empty.",
                nameof(sortedValues));
        }

        if (percentile is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percentile));
        }

        double position =
            percentile * (sortedValues.Count - 1);

        int lowerIndex = (int)Math.Floor(position);
        int upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        double weight = position - lowerIndex;

        return sortedValues[lowerIndex]
            + ((sortedValues[upperIndex] - sortedValues[lowerIndex])
               * weight);
    }

    private double NextGaussian()
    {
        // Box-Muller transform.
        double u1 = 1.0 - _random.NextDouble();
        double u2 = 1.0 - _random.NextDouble();

        return Math.Sqrt(-2.0 * Math.Log(u1))
            * Math.Cos(2.0 * Math.PI * u2);
    }

    private static void ValidateSimulationArguments(
        int simulations,
        int steps)
    {
        if (simulations <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(simulations),
                "Simulation count must be greater than zero.");
        }

        if (steps <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(steps),
                "Step count must be greater than zero.");
        }
    }
}