using Azure.Storage.Blobs;
using FibonacciTrade.Algorithm.MonteCarlo;
using FibonacciTrade.Algorithm.MonteCarlo.Objects;
using FibonacciTrade.ALgorithm.CoeficientRetriever;
using FibonacciTrade.ALgorithm.EconomicCalendar;
using FibonacciTrade.ALgorithm.EconomicCalendar.Objects;
using FibonacciTrade.ALgorithm.LearnedContainerAction;
using FibonacciTrade.ALgorithm.RNN;
using FibonacciTrade.Data;
using FibonacciTrade.Data.Access;
using FibonacciTrade.Data.Algorithm.Objects;
using FibonacciTrade.Data.DataCollection.Objects;
using FibonacciTrade.Data.Oanda;
using FibonacciTrade.Data.Oanda.CosmosObjects;
using FibonacciTrade.Data.Oanda.Objects;
using FibonacciTrade.Logic;
using fxtrade.entities;
using fxtrade.entities.Entities;
using fxtrade.entities.Entities.fxtrade.entities.Models;
using fxtrade.entities.Migrations;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System.Text.Json;


namespace FibonacciTrade.ALgorithm
{
    public class KNN
    {
        private ICoefficientRetriever retriever { get; set; }
        private double allowedReward = -0.001;
        private const int InstrumentCount = 68;
        private const int FeaturesPerInstrument = 5; // O,H,L,C,V
        private const int FeatureCount = InstrumentCount * FeaturesPerInstrument;
        private const int WarmupRows = 10;

        private readonly BlobContainerClient blobContainerClient;
        private readonly CosmosClient cosmosClient;
        private readonly OandaClient oandaClient;
        BlobCollector blobCollector { get; set; }
        Container container { get; set; }
        KeyVaultObject kvo = new KeyVaultObject();
        private readonly TradingSiteDbContext _context;
        private readonly ITradeGate _tradeGate;
        private readonly RewardModelService _rewardModelService;
        private readonly MonteCarloPredictionStore _monteCarloStore;
        EconomicCalendarService economicCalendarService { get; set; }
        private readonly ILogger<KNN>? _logger;
        private readonly IMonteCarloPredictionService
    _monteCarloPredictionService;
        public KNN(string id, int accountIndex, string setting, Dictionary<string, ForwardProjection> forwardProjections, TradingSiteDbContext context, ITradeGate _tradeGate, RewardModelService _rewardModelService, MonteCarloPredictionStore monteCarloStore, IMonteCarloPredictionService predictionService, ILogger logger = null)
        {
            projections = forwardProjections;

            cosmosClient = new CosmosClient(kvo.CosmosConnStr);
            oandaClient = new OandaClient(new OandaObject(id), accountIndex, setting);
            string blobcontainer = "historical-data";

            blobContainerClient = new BlobContainerClient(
                kvo.BlobConnStr,
                blobcontainer);
            container = cosmosClient.GetDatabase("oanda").GetContainer("slices");
            blobCollector = new BlobCollector();
            _context = context;
            this._tradeGate = _tradeGate;
            this._rewardModelService = _rewardModelService;
            this._logger = _logger;
            _monteCarloStore = monteCarloStore;
            _monteCarloPredictionService = predictionService;
            economicCalendarService = new EconomicCalendarService(cosmosClient);
            this.retriever = new CoefficientRetriever(cosmosClient);
        }
        public KNN(
    string id,
    int accountIndex,
    string setting,
    TradingSiteDbContext context,
    ITradeGate tradeGate,
    RewardModelService rewardModelService,
    MonteCarloPredictionStore monteCarloStore,
    IMonteCarloPredictionService predictionService,
    ILogger<KNN>? logger = null)
        {
            cosmosClient = new CosmosClient(kvo.CosmosConnStr);

            oandaClient = new OandaClient(
                new OandaObject(id),
                accountIndex,
                setting);

            blobContainerClient = new BlobContainerClient(
                kvo.BlobConnStr,
                "historical-data");

            container = cosmosClient
                .GetDatabase("oanda")
                .GetContainer("slices");

            blobCollector = new BlobCollector();

            _context = context;
            this._tradeGate = tradeGate;
            this._rewardModelService = rewardModelService;
            _monteCarloStore = monteCarloStore;
            _logger = logger;
            _monteCarloPredictionService = predictionService;
            economicCalendarService = new EconomicCalendarService(cosmosClient);
            this.retriever = new CoefficientRetriever(cosmosClient);

        }
        //Intent is to read from cosmos, some file names, and download them, to compare them against the current data set which must be collected in the same way
        // 1. Collect Current Data
        // 2. Query Cosmos for File Names
        // 3. Download blobs as memory streams to deserialize to CosmosSlices 
        #region createCosmosSlice

        public async Task<CandlesContract> CreateCandlesContract(string granularity)
        {
            CandlesContract candlesContract = new CandlesContract() { id = Guid.NewGuid().ToString(), year = DateTime.Now.Year.ToString() };
            InstrumentRoot instrumentRoot = await oandaClient.GetInstruments();
            List<Data.Oanda.Objects.Instrument> instruments = instrumentRoot.instruments.OrderBy(ins => ins.name).ToList();
            Dictionary<string, InstrumentCandleContract> instrumentCandleContracts = new Dictionary<string, InstrumentCandleContract>();
            foreach (Data.Oanda.Objects.Instrument instrument in instruments)
            {
                instrumentCandleContracts[instrument.name] = new InstrumentCandleContract()
                {
                    instrument = instrument,
                    candles = await oandaClient.GetCandles(instrument, DateTime.Now, granularity, current: true)
                };
            }
            candlesContract.instrumentCandles = instrumentCandleContracts;
            return candlesContract;
        }
        public MatrixContainer CreateUnfilteredCandleMatrix(string granularity, CandlesContract candleContract)
        {

            int units = Statics.FibonacciWindow;
            int instrumentCount = InstrumentCount;
            decimal[][] candleMatrix = new decimal[units][];
            int matrixRow = 0;
            {
                int row = 0;
                while (row < Statics.FibonacciWindow)
                {
                    decimal[] innerCol = new decimal[FeatureCount];

                    for (int instr = 0; instr < instrumentCount; instr++)
                    {

                        string instrument = candleContract.instrumentCandles.Keys.ToList()[instr];
                        Candle candle = candleContract.instrumentCandles[instrument].candles.candles[row];
                        innerCol[5 * instr] = (decimal)Convert.ToDouble(candle.mid.o);
                        innerCol[5 * instr + 1] = (decimal)Convert.ToDouble(candle.mid.h);
                        innerCol[5 * instr + 2] = (decimal)Convert.ToDouble(candle.mid.l);
                        innerCol[5 * instr + 3] = (decimal)Convert.ToDouble(candle.mid.c);
                        innerCol[5 * instr + 4] = (decimal)Convert.ToDouble(candle.volume);
                    }
                    row++;
                    candleMatrix[matrixRow] = innerCol;
                    matrixRow++;

                }

            }
            decimal[][] retMat = new decimal[Statics.FibonacciWindow][];
            int rowx = 0;
            for (int x = 0; x < Statics.FibonacciWindow; x++)
            {
                decimal[] innerRow = new decimal[FeatureCount];
                for (int y = 0; y < FeatureCount; y++)
                {
                    innerRow[y] = candleMatrix[x][y];
                }
                retMat[rowx] = innerRow;
                rowx++;
            }
            MatrixContainer matrixContainer = new MatrixContainer() { granularity = granularity, matrix = retMat };
            return matrixContainer;
        }
        public MatrixContainer CreateRollingDeltaCandleMatrix(string granularity, MatrixContainer matrixContainer)
        {
            // Rolling Delta is one layer, another layer is rolling over average
            decimal[][] matrix = matrixContainer.matrix;
            int numRows = matrix.Where(row => row != null).Count();
            int ma = matrix.Length;
            decimal[][] rollingMatrix = new decimal[numRows][];
            for (int x = 0; x < numRows; x++)
            {
                decimal[] innerRow = new decimal[FeatureCount];
                for (int y = 0; y < FeatureCount; y++)
                {
                    if (x == 0)
                    {
                        innerRow[y] = 0;
                    }
                    else
                    {
                        decimal previous = matrix[x - 1][y];

                        innerRow[y] = previous == 0
                            ? 0
                            : matrix[x][y] / previous;
                    }
                }
                rollingMatrix[x] = innerRow;
            }
            decimal[][] retMat = new decimal[Statics.FibonacciWindow][];

            MatrixContainer rollingMatrixContainer = new MatrixContainer { granularity = granularity, matrix = rollingMatrix };
            return rollingMatrixContainer;
        }


        public async Task<OverAverageNumbersDocument?> GetOverAverageNumbersAsync(
            Container container, string granularity)
        {
            try
            {
                ItemResponse<OverAverageNumbersDocument> response =
                    await container.ReadItemAsync<OverAverageNumbersDocument>(
                        id: $"overaverageNumbers_{granularity}",
                        partitionKey: new PartitionKey("overaveragenumbers"));

                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }
        public async Task<MatrixContainer> CreateRollingOverAverageCandleMatrix(string granularity, MatrixContainer matrixContainer)
        {
            // Rolling Delta is one layer, another layer is rolling over average
            decimal[][] matrix = matrixContainer.matrix;
            int numRows = matrix.Where(row => row != null).Count();
            decimal[][] rollingMatrix = new decimal[numRows][];
            OverAverageNumbersDocument? doc = await GetOverAverageNumbersAsync(cosmosClient.GetDatabase("oanda").GetContainer("metadata"), granularity);

            if (doc == null)
            {
                throw new InvalidOperationException(
                    $"overaverageNumbers_{granularity} not found");
            }

            decimal[] avgArray = doc.numbers;

            for (int x = 0; x < numRows; x++)
            {
                decimal[] innerRow = new decimal[FeatureCount];
                for (int y = 0; y < FeatureCount; y++)
                {
                    decimal average = avgArray[y];

                    innerRow[y] = average == 0
                        ? 0
                        : matrix[x][y] / average;
                }
                rollingMatrix[x] = innerRow;
            }

            MatrixContainer rollingMatrixContainer = new MatrixContainer { granularity = granularity, matrix = rollingMatrix };
            return rollingMatrixContainer;
        }
        public MatrixContainer CreateMovingAverageN(string granularity, int n, MatrixContainer matrixContainer)
        {
            decimal[][] matrix = matrixContainer.matrix;

            int rowCount = matrix.Length;
            int featureCount = matrix[0].Length;

            decimal[][] movingAverageMatrix = new decimal[rowCount][];

            for (int x = 0; x < rowCount; x++)
            {
                decimal[] innerRow = new decimal[featureCount];

                for (int y = 0; y < featureCount; y++)
                {
                    decimal sum = 0;
                    int count = 0;

                    for (int z = x - n; z < x; z++)
                    {
                        if (z < 0)
                            continue;

                        sum += matrix[z][y];
                        count++;
                    }

                    innerRow[y] = count == n
                        ? sum / n
                        : 0;
                }

                movingAverageMatrix[x] = innerRow;
            }

            return new MatrixContainer
            {
                granularity = granularity,
                matrix = movingAverageMatrix
            };
        }
        decimal[][] ConstructCorrelationMatrix(decimal[][] candles)
        {
            decimal[][] correlationMatrix = new decimal[candles[0].Length][];
            for (int i = 0; i < candles[0].Length; i++)
            {
                decimal[] innerRow = new decimal[candles[0].Length];
                for (int j = 0; j < candles[0].Length; j++)
                {
                    innerRow[j] = Statistics.Correlation(candles, i, j);
                }
                correlationMatrix[i] = innerRow;
            }
            return correlationMatrix;
        }
        private static decimal[][] TrimWarmupRows(decimal[][] matrix)
        {
            return matrix
                .Skip(WarmupRows)
                .Select(row => row.ToArray())
                .ToArray();
        }
        public async Task<CosmosSlice> CollectCurrentData(string granularity)
        {
            CandlesContract candlesContract = await CreateCandlesContract(granularity);

            MatrixContainer candles = CreateUnfilteredCandleMatrix(granularity, candlesContract);

            MatrixContainer rollingAverage = CreateRollingDeltaCandleMatrix(granularity, candles);

            MatrixContainer rollingOverAverage = await CreateRollingOverAverageCandleMatrix(granularity, candles);

            MatrixContainer maRollingAverage5 = CreateMovingAverageN(granularity, 5, rollingAverage);

            MatrixContainer maRollingAverage10 = CreateMovingAverageN(granularity, 10, rollingAverage);

            MatrixContainer maRollingOverAverage5 = CreateMovingAverageN(granularity, 5, rollingOverAverage);

            MatrixContainer maRollingOverAverage10 = CreateMovingAverageN(granularity, 10, rollingOverAverage);

            decimal[][] correlationMatrix = ConstructCorrelationMatrix(candles.matrix);

            CosmosSlice slice = new CosmosSlice()
            {
                candles = TrimWarmupRows(candles.matrix),

                correlation = correlationMatrix,

                deltas = TrimWarmupRows(rollingAverage.matrix),

                granularity = granularity,

                id = "1",

                index = 0,

                MADelta5 = TrimWarmupRows(maRollingAverage5.matrix),

                MADelta10 = TrimWarmupRows(maRollingAverage10.matrix),

                MAOverAverage5 = TrimWarmupRows(maRollingOverAverage5.matrix),

                MAOverAverage10 = TrimWarmupRows(maRollingOverAverage10.matrix),

                overAverages = TrimWarmupRows(rollingOverAverage.matrix)
            };

            return slice;
        }
        #endregion

        #region runKNNAlgorithm
        // Blob Collector runs the difference algorithm, I need to collect pointers from Cosmos and then pass those into the collector
        public async Task<List<CosmosPointer>> GetRandomPointersAsync(
    Container container,
    string granularity,
    int count)
        {
            var pointers = new List<CosmosPointer>();

            var query = new QueryDefinition(
                "SELECT c.id, c.granularity, c.index, c.uri " +
                "FROM c WHERE c.granularity = @granularity and c.index < 15000")
                .WithParameter("@granularity", granularity);

            using FeedIterator<CosmosPointer> iterator =
                container.GetItemQueryIterator<CosmosPointer>(
                    query,
                    requestOptions: new QueryRequestOptions
                    {
                        PartitionKey = new PartitionKey(granularity)
                    });

            while (iterator.HasMoreResults)
            {
                FeedResponse<CosmosPointer> response =
                    await iterator.ReadNextAsync();

                pointers.AddRange(response);
            }

            return pointers
                .OrderBy(_ => Random.Shared.Next())
                .Take(Math.Min(count, pointers.Count))
                .ToList();
        }

        public List<TradeCandidate> BuildTradeCandidates(
    ForwardProjection projection,
    bool[] tradeMask)
        {
            var results = new List<TradeCandidate>();

            for (int i = 0; i < projection.Mean.Length; i++)
            {
                if (!tradeMask[i])
                    continue;

                decimal meanRatio = projection.Mean[i];

                // Mean is centered around 1.0, so convert it to signed forward movement.
                decimal forward = meanRatio - 1m;

                if (forward == 0m)
                    continue;

                decimal variance = projection.Variance[i];

                decimal stdDev =
                    (decimal)Math.Sqrt((double)Math.Max(variance, 0m));

                decimal confidence =
                    Math.Abs(forward) /
                    Math.Max(stdDev, 0.000001m);

                results.Add(new TradeCandidate
                {
                    InstrumentIndex = i,
                    Forward = forward,
                    Variance = variance,
                    StdDev = stdDev,
                    Confidence = confidence
                });
            }

            return results
                .OrderByDescending(x => x.Confidence)
                .ToList();
        }
        public static List<ConsensusTradeCandidate> FindTopConsensusTrades(
          Dictionary<string, List<TradeCandidate>> candidatesByGranularity,
          string[] requiredGranularities,
          int perGranularityPoolSize = 25,
          int finalCount = 10,
          string[]? instrumentNames = null)
        {
            if (candidatesByGranularity == null)
                throw new ArgumentNullException(nameof(candidatesByGranularity));

            if (requiredGranularities == null || requiredGranularities.Length == 0)
                throw new ArgumentException(
                    "At least one granularity is required.",
                    nameof(requiredGranularities));

            var topByGranularity = new Dictionary<string, List<TradeCandidate>>();

            foreach (string granularity in requiredGranularities)
            {
                if (!candidatesByGranularity.TryGetValue(granularity, out var candidates))
                    return new List<ConsensusTradeCandidate>();

                var top = candidates
                    .Where(x => x.Forward != 0m)
                    .OrderByDescending(x => x.Confidence)
                    .ThenByDescending(x => Math.Abs(x.Forward))
                    .Take(perGranularityPoolSize)
                    .ToList();

                topByGranularity[granularity] = top;
            }

            // IMPORTANT:
            // Use the union of all top candidate indexes, not just H4.
            var candidateIndexes = requiredGranularities
                .SelectMany(g => topByGranularity[g].Select(x => x.InstrumentIndex))
                .Distinct()
                .ToList();

            var consensus = new List<ConsensusTradeCandidate>();

            foreach (int instrumentIndex in candidateIndexes)
            {
                var perGranularity = new Dictionary<string, TradeCandidate>();

                bool foundInAll = true;

                foreach (string granularity in requiredGranularities)
                {
                    var match = topByGranularity[granularity]
                        .FirstOrDefault(x => x.InstrumentIndex == instrumentIndex);

                    if (match == null)
                    {
                        foundInAll = false;
                        break;
                    }

                    perGranularity[granularity] = match;
                }

                if (!foundInAll)
                    continue;

                int? agreedSign = null;
                bool signsAgree = true;

                foreach (var candidate in perGranularity.Values)
                {
                    int sign = GetSign(candidate.Forward);

                    if (sign == 0)
                    {
                        signsAgree = false;
                        break;
                    }

                    if (agreedSign == null)
                    {
                        agreedSign = sign;
                    }
                    else if (agreedSign.Value != sign)
                    {
                        signsAgree = false;
                        break;
                    }
                }

                if (!signsAgree || agreedSign == null)
                    continue;

                decimal averageForward = perGranularity.Values
                    .Average(x => x.Forward);

                decimal minConfidence = perGranularity.Values
                    .Min(x => x.Confidence);

                decimal averageConfidence = perGranularity.Values
                    .Average(x => x.Confidence);

                decimal combinedAbsForward = perGranularity.Values
                    .Sum(x => Math.Abs(x.Forward));

                string? instrumentName = null;

                if (instrumentNames != null &&
                    instrumentIndex >= 0 &&
                    instrumentIndex < instrumentNames.Length)
                {
                    instrumentName = instrumentNames[instrumentIndex];
                }

                DateTime generatedUtc = DateTime.UtcNow;

                consensus.Add(new ConsensusTradeCandidate
                {
                    InstrumentIndex = instrumentIndex,
                    InstrumentName = instrumentName,
                    Direction = agreedSign.Value,
                    AverageForward = averageForward,
                    MinConfidence = minConfidence,
                    AverageConfidence = averageConfidence,
                    CombinedAbsForward = combinedAbsForward,
                    ByGranularity = perGranularity,
                    GeneratedUtc = generatedUtc
                });
            }

            return consensus
                .OrderByDescending(x => x.MinConfidence)
                .ThenByDescending(x => x.CombinedAbsForward)
                .ThenByDescending(x => x.AverageConfidence)
                .Take(finalCount)
                .ToList();
        }
        private static int GetSign(decimal value)
        {
            if (value > 0m)
                return 1;

            if (value < 0m)
                return -1;

            return 0;
        }
        public async Task<ForwardProjection> CalculateForwards(string granularity, int count)
        {
            List<CosmosPointer> cosmosPointers = await GetRandomPointersAsync(container, granularity, count);
            CosmosSlice currentSlice = await CollectCurrentData(granularity);
            ForwardProjection forwardProjection = await blobCollector.ComparisonAlgorithm(cosmosPointers, currentSlice);
            return forwardProjection;
        }
        public string[] instrumentNames { get; set; }
        public string[] allowedInstruments { get; set; }
        public int topN = 10;
        public string[] requiredGranularities { get; set; }
        public Dictionary<string, ForwardProjection> projections { get; set; }
        public List<ConsensusTradeCandidate> ConsensusTradeCandidates { get; set; }
        public bool RewardGateApproved { get; set; }
        private async Task<MonteCarloPredictionDocument> GetOrCreateMonteCarloAsync(
ConsensusTradeCandidate trade,
CancellationToken cancellationToken)
        {
            var monteCarlo =
                await _monteCarloStore.GetAsync(
                    trade.InstrumentName,
                    cancellationToken);

            if (monteCarlo != null)
            {
                return monteCarlo;
            }

            monteCarlo =
                await _monteCarloPredictionService.GenerateAsync(
                    trade, simulations: 5000,
                    steps: 10,
                    cancellationToken);

            await _monteCarloStore.UpsertAsync(
                monteCarlo,
                cancellationToken);

            return monteCarlo;
        }

        public async Task CalculateTradeCandidates(
    int count,
    CancellationToken cancellationToken = default)
        {
            var instrumentRoot =
                await oandaClient.GetInstruments();
            var account = await oandaClient.GetAccount();
            List<Data.Oanda.Objects.Instrument> instruments =
                instrumentRoot.instruments
                    .OrderBy(ins => ins.name)
                    .ToList();

            instrumentNames =
                instruments
                    .Select(ins => ins.name)
                    .ToArray();

            allowedInstruments =
                instruments
                    .Select(ins => ins.name)
                    .Where(ins =>
                        !ins.Contains("CNH") &&
                        !ins.Contains("DKK") &&
                        !ins.Contains("PLN") &&
                        !ins.Contains("CZK") &&
                        !ins.Contains("HKD") &&
                        !ins.Contains("TRY"))
                    .ToArray();

            HashSet<string> allowedInstrumentSet =
                allowedInstruments.ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

            bool[] tradeMask =
                instruments
                    .Select(ins =>
                        allowedInstrumentSet.Contains(ins.name))
                    .ToArray();

            var dProjection =
                await CalculateForwards("D", count);

            var h8Projection =
                await CalculateForwards("H8", count);

            var h4Projection =
                await CalculateForwards("H4", count);

            projections =
                new Dictionary<string, ForwardProjection>
                {
                    ["D"] = dProjection,
                    ["H8"] = h8Projection,
                    ["H4"] = h4Projection
                };

            List<TradeCandidate> h4Candidates =
                BuildTradeCandidates(
                    h4Projection,
                    tradeMask);

            List<TradeCandidate> h8Candidates =
                BuildTradeCandidates(
                    h8Projection,
                    tradeMask);

            List<TradeCandidate> dCandidates =
                BuildTradeCandidates(
                    dProjection,
                    tradeMask);

            var byGranularity =
                new Dictionary<string, List<TradeCandidate>>
                {
                    ["H4"] = h4Candidates,
                    ["H8"] = h8Candidates,
                    ["D"] = dCandidates
                };

            string[] requiredGranularities =
            [
                "H4",
        "H8",
        "D"
            ];

            this.requiredGranularities =
                requiredGranularities;

            List<ConsensusTradeCandidate> consensus =
                FindTopConsensusTrades(
                    byGranularity,
                    requiredGranularities,
                    perGranularityPoolSize: 30,
                    finalCount: 10,
                    instrumentNames: instrumentNames);

            ConsensusTradeCandidates = consensus;

            /*
             * The account state is shared by the candidates in this run.
             *
             * If an order is successfully placed, update the local state so
             * later candidates see a more conservative exposure/trade count.
             */
            var learnedContainer = await LearnedContainerClass.ReturnLearnedContainer(cosmosClient);

            var accountState =
                await oandaClient.GetTradingAccountState();

            foreach (var trade in consensus)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string direction =
          trade.Direction >= 0
                       ? "LONG"
                       : "SHORT";

                string name = trade.InstrumentName;

                bool monteCarloApproved;

                decimal currentSpreadPips;

                try
                {
                    currentSpreadPips =
                        await oandaClient.GetCurrentSpreadPips(name);
                }
                catch (Exception ex)
                {
                    /*
                     * A missing spread is a risk-data failure.
                     * Record a failed decision and do not place the order.
                     */
                    var spreadFailureDecision =
                        CreateFailedTradeDecision(
                           trade,
                            accountState,
                            name,
                    $"Unable to retrieve spread: {ex.Message}");

                    _context.TradeDecisions.Add(
              spreadFailureDecision);

                    await _context.SaveChangesAsync(
                        cancellationToken);

                    Console.WriteLine(
                        $"RL-GATE ERROR {name}: " +
                    spreadFailureDecision.Reason);
                    continue;
                }

                var gateContext =
                         new TradeGateContext
                         {
                             EvaluatedAtUtc = DateTimeOffset.UtcNow,
                             AccountId = accountState.AccountId,

                             IsPracticeAccount =
                    accountState.IsPractice,

                             AccountBalance =
                    accountState.Balance,

                             CurrentOpenExposure =
                    accountState.OpenExposure,

                             OpenTradeCount =
                    accountState.OpenTradeCount,

                             CurrentSpreadPips =
                    currentSpreadPips,

                             AllowThreshold =
                    0.65f,

                             /*
                              * Keep true while collecting training data.
                              */
                             ShadowMode =
                    false
                         }
            ;

                TradeDecision decision;
                var declineSvd = false;
                try
                {
                    var monteCarlo = await GetOrCreateMonteCarloAsync(
    trade,
    cancellationToken);

                    /*
                     * Do not use a prediction generated before this candidate
                     * projection. Otherwise the gate could combine a new candidate
                     * with an old simulated market state.
                     */
                    if (monteCarlo.CreatedUtc < trade.GeneratedUtc)
                    {
                        throw new InvalidOperationException(
                            $"Monte Carlo prediction for {trade.InstrumentName} " +
                            $"is stale. Prediction: {monteCarlo.CreatedUtc:O}; " +
                            $"candidate: {trade.GeneratedUtc:O}.");
                    }
                    #region state
                    bool isLong = trade.Direction > 0;

                    double directionalReturn =
                        isLong
                            ? monteCarlo.MeanReturn
                            : -monteCarlo.MeanReturn;

                    double directionalProbProfit =
                        isLong
                            ? monteCarlo.ProbProfit
                            : 1.0 - monteCarlo.ProbProfit;



                    decimal originalAverageForward = trade.AverageForward;
                    decimal originalScore = trade.AverageConfidence;

                    double directionalMeanReturn =
                        trade.Direction > 0
                            ? monteCarlo.MeanReturn
                            : -monteCarlo.MeanReturn;

                    double directionalProbability =
                        trade.Direction > 0
                            ? monteCarlo.ProbProfit
                            : 1.0 - monteCarlo.ProbProfit;

                    double directionalWorstDecile =
                        trade.Direction > 0
                            ? monteCarlo.WorstDecile
                            : -monteCarlo.BestDecile;

                    double directionalBestDecile =
                        trade.Direction > 0
                            ? monteCarlo.BestDecile
                            : -monteCarlo.WorstDecile;

                    double directionalRiskAdjustedReturn =
                        directionalMeanReturn /
                        Math.Max(monteCarlo.StdDev, 0.000001);

                    double monteCarloScore =
                        directionalRiskAdjustedReturn *
                        directionalProbability *
                        Math.Max((double)trade.AverageConfidence, 0.05);
                    monteCarloApproved =
                         directionalProbProfit >= 0.55 &&
                         directionalRiskAdjustedReturn > 0 &&
                         directionalWorstDecile > -0.005;
                    /*
                     * Replace the original KNN candidate return and ranking score.
                     *
                     * Keep the original values in separate properties if you want
                     * them for future training and diagnostics.
                     */
                    trade.OriginalAverageForward = trade.AverageForward;
                    trade.OriginalScore = trade.Score;

                    //                    trade.AverageForward = (decimal)directionalReturn;
                    trade.Score = (decimal)monteCarloScore;

                    /*
                     * If gateContext duplicates these values, update it too.
                     * Otherwise EvaluateAsync could still use the original values.
                     */
                    gateContext.AverageForward = (decimal)directionalReturn;
                    gateContext.CandidateScore = (decimal)monteCarloScore;
                    gateContext.ProbabilityOfProfit = (decimal)directionalProbProfit;
                    gateContext.WorstDecile = (decimal)directionalWorstDecile;
                    gateContext.BestDecile = (decimal)directionalBestDecile;
                    gateContext.ReturnStdDev = (decimal)monteCarlo.StdDev;

                    decision =
                        await _tradeGate.EvaluateAsync(
                            trade,
                            gateContext,
                            cancellationToken);

                    /*
                     * Copy the Monte Carlo state into the persisted TradeDecision.
                     */
                    decision.MonteCarloMeanReturn =
                        (decimal)directionalReturn;

                    decision.MonteCarloStdDev =
                        (decimal)monteCarlo.StdDev;

                    decision.MonteCarloProbabilityOfProfit =
                        (decimal)directionalProbProfit;

                    decision.MonteCarloWorstDecile =
                        (decimal)directionalWorstDecile;

                    decision.MonteCarloMedianReturn =
                        (decimal)(
                            isLong
                                ? monteCarlo.MedianReturn
                                : -monteCarlo.MedianReturn);

                    decision.MonteCarloBestDecile =
                        (decimal)directionalBestDecile;

                    decision.MonteCarloRiskAdjustedReturn =
                        (decimal)directionalRiskAdjustedReturn;

                    decision.MonteCarloScore =
                        (decimal)monteCarloScore;

                    decision.MonteCarloSimulationCount =
                        monteCarlo.SimulationCount;

                    decision.MonteCarloSteps =
                        monteCarlo.Steps;

                    decision.MonteCarloCalculatedUtc =
                        monteCarlo.CreatedUtc;
                    #endregion
                    #region features

                    var features = new
                    {
                        accountState.NetAssetValue,
                        accountState.Balance,
                        accountState.MarginAvailable,
                        accountState.MarginUsed,
                        accountState.OpenExposure,
                        accountState.OpenTradeCount,
                        accountState.UnrealizedPnL,

                        // Keep your existing position PNL/unit features here.
                        AUD_CADPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_CAD")),
                        AUD_CADUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_CAD")),
                        AUD_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_CHF")),
                        AUD_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_CHF")),
                        AUD_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_JPY")),
                        AUD_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_JPY")),
                        AUD_NZDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_NZD")),
                        AUD_NZDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_NZD")),
                        AUD_SGDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_SGD")),
                        AUD_SGDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_SGD")),
                        AUD_USDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_USD")),
                        AUD_USDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_USD")),
                        CAD_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "CAD_CHF")),
                        CAD_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "CAD_CHF")),
                        CAD_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "CAD_JPY")),
                        CAD_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "CAD_JPY")),
                        CAD_SGDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "CAD_SGD")),
                        CAD_SGDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "CAD_SGD")),
                        CHF_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "CHF_JPY")),
                        CHF_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "CHF_JPY")),
                        CHF_ZARPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "CHF_ZAR")),
                        CHF_ZARUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "CHF_ZAR")),
                        EUR_AUDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_AUD")),
                        EUR_AUDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_AUD")),
                        EUR_CADPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_CAD")),
                        EUR_CADUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_CAD")),
                        EUR_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_CHF")),
                        EUR_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_CHF")),
                        EUR_GBPPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_GBP")),
                        EUR_GBPUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_GBP")),
                        EUR_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_JPY")),
                        EUR_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_JPY")),
                        EUR_NZDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_NZD")),
                        EUR_NZDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_NZD")),
                        EUR_SEKPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_SEK")),
                        EUR_SEKUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_SEK")),
                        EUR_SGDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_SGD")),
                        EUR_SGDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_SGD")),
                        EUR_TRYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_TRY")),
                        EUR_TRYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_TRY")),
                        EUR_USDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_USD")),
                        EUR_USDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_USD")),
                        EUR_ZARPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_ZAR")),
                        EUR_ZARUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_ZAR")),
                        GBP_AUDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_AUD")),
                        GBP_AUDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_AUD")),
                        GBP_CADPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_CAD")),
                        GBP_CADUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_CAD")),
                        GBP_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_CHF")),
                        GBP_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_CHF")),
                        GBP_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_JPY")),
                        GBP_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_JPY")),
                        GBP_NZDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_NZD")),
                        GBP_NZDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_NZD")),
                        GBP_SGDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_SGD")),
                        GBP_SGDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_SGD")),
                        GBP_USDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_USD")),
                        GBP_USDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_USD")),
                        GBP_ZARPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_ZAR")),
                        GBP_ZARUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_ZAR")),
                        NZD_CADPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_CAD")),
                        NZD_CADUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_CAD")),
                        NZD_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_CHF")),
                        NZD_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_CHF")),
                        NZD_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_JPY")),
                        NZD_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_JPY")),
                        NZD_SGDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_SGD")),
                        NZD_SGDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_SGD")),
                        NZD_USDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_USD")),
                        NZD_USDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_USD")),
                        SGD_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "SGD_CHF")),
                        SGD_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "SGD_CHF")),
                        SGD_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "SGD_JPY")),
                        SGD_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "SGD_JPY")),
                        TRY_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "TRY_JPY")),
                        TRY_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "TRY_JPY")),
                        USD_CADPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_CAD")),
                        USD_CADUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_CAD")),
                        USD_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_CHF")),
                        USD_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_CHF")),
                        USD_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_JPY")),
                        USD_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_JPY")),
                        USD_MXNPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_MXN")),
                        USD_MXNUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_MXN")),
                        USD_SEKPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_SEK")),
                        USD_SEKUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_SEK")),
                        USD_SGDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_SGD")),
                        USD_SGDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_SGD")),
                        USD_THBPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_THB")),
                        USD_THBUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_THB")),
                        USD_TRYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_TRY")),
                        USD_TRYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_TRY")),
                        USD_ZARPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_ZAR")),
                        USD_ZARUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_ZAR")),
                        ZAR_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "ZAR_JPY")),
                        ZAR_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "ZAR_JPY")),
                        Symbol = trade.InstrumentName,

                        decision.FeatureSchemaVersion,
                        decision.AccountBalance,

                        /*
                         * Preserve KNN confidence and variance as context.
                         * You are replacing its return, not necessarily discarding
                         * all information from the projection model.
                         */
                        decision.AverageConfidence,
                        decision.MinimumConfidence,
                        decision.CurrentOpenExposure,
                        decision.CurrentSpreadPips,

                        decision.H4Confidence,
                        decision.H4Variance,

                        decision.H8Confidence,
                        decision.H8Variance,

                        decision.DConfidence,
                        decision.DVariance,

                        GeneratedUtc = DateTime.UtcNow,

                        /*
                         * Monte Carlo now supplies return and score.
                         */
                        AverageForward = directionalReturn,
                        CandidateScore = monteCarloScore,

                        MonteCarloMeanReturn =
                            directionalReturn,

                        MonteCarloStdDev =
                            monteCarlo.StdDev,

                        MonteCarloProbabilityOfProfit =
                            directionalProbProfit,

                        MonteCarloWorstDecile =
                            directionalWorstDecile,

                        MonteCarloMedianReturn =
                            isLong
                                ? monteCarlo.MedianReturn
                                : -monteCarlo.MedianReturn,

                        MonteCarloBestDecile =
                            directionalBestDecile,

                        MonteCarloRiskAdjustedReturn =
                            directionalRiskAdjustedReturn,

                        MonteCarloReturnSpread =
                            directionalBestDecile -
                            directionalWorstDecile,

                        MonteCarloSimulationCount =
                            monteCarlo.SimulationCount,

                        MonteCarloSteps =
                            monteCarlo.Steps,

                        MonteCarloCalculatedUtc =
                            monteCarlo.CreatedUtc,
                        TradeDirection = trade.Direction,

                        // Raw market expectation
                        AverageForwardRaw =
    monteCarlo.MeanReturn,

                        MonteCarloMeanReturnRaw =
    monteCarlo.MeanReturn,

                        // Trade-adjusted expectation
                        AverageForwardDirectional =
    directionalReturn,

                        MonteCarloMeanReturnDirectional =
    directionalReturn,


                        // Agreement between forecast and trade direction
                        SignalAgreement =
    Math.Sign((double)monteCarlo.MeanReturn) ==
    Math.Sign(trade.Direction)
        ? 1
        : -1,
                        h4CandleDistance = h4Projection.Stats[0],
                        h4DeltaDistance = h4Projection.Stats[1],
                        h4OverAverageDistance = h4Projection.Stats[2],
                        h4MaDelta5Distance = h4Projection.Stats[3],
                        h4MaDelta10Distance = h4Projection.Stats[4],
                        h4MaOverAverage5Distance = h4Projection.Stats[5],
                        h4MaOverAverage10Distance = h4Projection.Stats[6],
                        h4CorrelationDistance = h4Projection.Stats[7],

                        h8CandleDistance = h8Projection.Stats[0],
                        h8DeltaDistance = h8Projection.Stats[1],
                        h8OverAverageDistance = h8Projection.Stats[2],
                        h8MaDelta5Distance = h8Projection.Stats[3],
                        h8MaDelta10Distance = h8Projection.Stats[4],
                        h8MaOverAverage5Distance = h8Projection.Stats[5],
                        h8MaOverAverage10Distance = h8Projection.Stats[6],
                        h8CorrelationDistance = h8Projection.Stats[7],

                        dCandleDistance = dProjection.Stats[0],
                        dDeltaDistance = dProjection.Stats[1],
                        dOverAverageDistance = dProjection.Stats[2],
                        dMaDelta5Distance = dProjection.Stats[3],
                        dMaDelta10Distance = dProjection.Stats[4],
                        dMaOverAverage5Distance = dProjection.Stats[5],
                        dMaOverAverage10Distance = dProjection.Stats[6],
                        dCorrelationDistance = dProjection.Stats[7]

                    };
                    #endregion
                    #region SVD
                    decision.FeatureJson =
                        JsonSerializer.Serialize(features);
                    var basis = await retriever
                        .GetBasisAsync(decision.InstrumentName);

                    Console.WriteLine($"basis null? {basis == null}");
                    // Existing features
                    var features_ =
                        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                            decision.FeatureJson)
                        ?? new Dictionary<string, JsonElement>();

                    // Build x
                    var x = BuildFeatureVector(
                        decision.FeatureJson,
                        basis.FeatureNames);

                    // Compute projection
                    double[] VkTx = ProjectOntoSubspace(
                        learnedContainer.RightSingularVectors,
                        x);

                    // Add coefficient coordinates
                    for (int i = 0; i < 41; i++)
                    {
                        features_[$"Coeff_{i + 1:D2}"] =
                            JsonSerializer.SerializeToElement(VkTx[i]);
                    }

                    features_["SVD_Energy"] =
                        JsonSerializer.SerializeToElement(
                            VkTx.Sum(v => v * v));

                    features_["SVD_MaxAbs"] =
                        JsonSerializer.SerializeToElement(
                            VkTx.Max(v => Math.Abs(v)));

                    features_["SVD_Mean"] =
                        JsonSerializer.SerializeToElement(
                            VkTx.Average());

                    features_["SVD_StdDev"] =
                        JsonSerializer.SerializeToElement(
                            Math.Sqrt(VkTx.Select(v => v * v).Average()));// Save back
                    decision.FeatureJson =
                        JsonSerializer.Serialize(features_); 
                    
                    var xAnyHigh = VkTx.Any(y => Math.Abs(y) > 599.001);
                    double energy =
    VkTx.Sum(v => v * v); 
                    //var xLowAny = VkTx.Any(y => Math.Abs(y) <= 10.2);
                    var vktxAvg = VkTx.Average();
                    bool tiltedUp = (VkTx.OrderByDescending(i=>i).Take(5).Average() + VkTx.Order().Take(5).Average()) / 2 > vktxAvg;
                    
                    var projection = ReconstructFromSubspace(
learnedContainer.RightSingularVectors,
VkTx);
                    var threshold = 1.5;
                    var residual =
                        projection.Zip(x, (p, xv) => (xv - p) * (xv - p))
                                  .Sum();

                    double residualNorm = Math.Sqrt(residual);
                    double positiveTail =
    VkTx.OrderByDescending(Math.Abs)
        .Take(5)
        .Average(Math.Abs);
                    if (residualNorm > threshold)
                    {
                        declineSvd = false;
                    
                        decision.Reason = AppendReason(decision.Reason, $"Singular avg {vktxAvg:F1}, high {VkTx.Max(Math.Abs):F1}, low {VkTx.Min(Math.Abs):F1}");
                        //continue;
                    }
                    else
                    {
                        Console.WriteLine("SVD Acceptable response");
                    }
                    //if (factorRank.Any(ranking => 
                    //{
                    //    return ranking.SingularValue < 094;


                    //})
                    //    )
                    //{ if (factorRank.All(ranking1 => {
                    //        return ranking1.SingularValue > 11.1 && xAnyHigh;
                    //    }))
                    //    {
                    //        AppendReason(decision.Reason,
                    //        String.Format("ranking1.MaxSingularvalue= {0}", x.Max()));
                    //            continue;
                    //            }}
                    //else
                    //{

                    //    AppendReason(decision.Reason, String.Format(factorRank.Any(ranking => ranking.SingularValue > 118.19).ToString()));
                    //    continue;
                    //}

                    Console.WriteLine(
    $"Pseudo Rows: {basis.Pseudoinverse?.Length}");

                    Console.WriteLine(
                        $"Pseudo Cols: {basis.Pseudoinverse?[0]?.Length}");

                    Console.WriteLine(
                        $"Vector Length: {x.Length}");


        /*            var coefficients =
                  await retriever.ComputeCoefficientsAsync(
                      decision.InstrumentName, //decision.InstrumentName,
                      x,
                      cancellationToken);
                    var vector = new CoefficientVector
                    {
                        DecisionId = decision.Id,
                        Instrument = decision.InstrumentName,
                        BasisVersion = basis.ModelVersion,
                        BasisRank = basis.Rank,
                        FeatureSchemaHash = basis.FeatureSchemaHash,
                        CreatedUtc = DateTime.UtcNow,
                        CoefficientJson = JsonSerializer.Serialize(coefficients)
                    };

                    //_context.CoefficientVectors.Add(vector);
                    //await _context.SaveChangesAsync();
                    /*var rows = await _context.CoefficientVectors
.Where(x => x.Reward != null)
.ToListAsync();
                                var nearest =
                 rows
                     .OrderBy(y => Distance(
                         x,
                         GetVector(y.CoefficientJson)))
                     .First();
                                Console.WriteLine(
    $"Features: {x.Length}");

                    Console.WriteLine(
                        $"PseudoInverse Rows: {basis.Pseudoinverse.Length}");

                    Console.WriteLine(
                        $"PseudoInverse Cols: {basis.Pseudoinverse[0].Length}");
                    
                    */


                    #endregion
                    PopulateTradeDecision(
                        decision,
                        trade,
                        gateContext,
                        name);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"PRE-RL-GATE ERROR {name}: {ex.Message}");

                    decision =
                        CreateFailedTradeDecision(
                            trade,
                            accountState,
                            name,
                            $"Trade gate evaluation failed: {ex.Message}");

                    decision.CurrentSpreadPips =
                        currentSpreadPips;

                    decision.AllowThreshold =
                        gateContext.AllowThreshold;

                    decision.ShadowMode =
                        gateContext.ShadowMode;
                    monteCarloApproved = false;
                }

                /*
                 * Save the initial state/action before attempting the order.
                 * This records skipped decisions and also preserves the
                 * decision if the OANDA order later fails.
                 */

                _context.TradeDecisions.Add(decision);

                await _context.SaveChangesAsync(cancellationToken);

                if (!decision.ShouldPlaceTrade && !decision.ShadowMode)
                {
                    //LogSkipped(name, "Rejected by trade gate.");
                    continue;
                }
                #region placeOrder
                double predictedReward;
                bool placeOrder;
                try
                {
                    predictedReward =
                        (double)_rewardModelService.Predict(
                            decision.FeatureJson);

                    decision.RLReward =
                        (decimal)Math.Round(
                            predictedReward,
                            7,
                            MidpointRounding.AwayFromZero);

                    bool rewardApproved =
                        predictedReward > allowedReward;
                    bool FinalTradeApproved =
                    decision.ShouldPlaceTrade && RewardGateApproved;

                    placeOrder = FinalTradeApproved && monteCarloApproved;

                    decision.Reason =
                        AppendReason(
                            decision.Reason,
                            rewardApproved
                                ? $"Reward gate approved: {predictedReward:F8}."
                                : $"Reward gate rejected: {predictedReward:F8}.");

                    await _context.SaveChangesAsync(
                        cancellationToken);
                    if (!monteCarloApproved)
                    {
                        decision.Reason = AppendReason(decision.Reason, $"Monte Carlo Denial for: profit proability {decision.MonteCarloProbabilityOfProfit}, wost decile {decision.MonteCarloWorstDecile}");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    decision.Reason =
                        AppendReason(
                            decision.Reason,
                            $"Reward inference failed: {ex.Message}");

                    await _context.SaveChangesAsync(
                        cancellationToken);

                    _logger?.LogError(
                        ex,
                        "Reward inference failed for {Symbol}",
                        name);

                    continue;
                }

                if (decision.ShadowMode)
                {
                    if (!decision.IsPracticeAccount)
                    {
                        Console.WriteLine(
                            $"BLOCKED {name}: shadow-mode orders " +
                            "are restricted to practice accounts.");

                        continue;
                    }

                    placeOrder = true;
                }
                else
                {
                    placeOrder =
                        decision.ShouldPlaceTrade;
                }
                var events =
    await economicCalendarService
        .GetRelevantEventsAsync(
            trade.InstrumentName.Substring(0,3),
            trade.InstrumentName.Substring(4,3),
            DateTime.UtcNow);

                decision.NewsRisk =
                    CalculateNewsRisk(
                        events,
                        DateTime.UtcNow);
                if (events.Any(x =>
                    IsInNewsBlackout(
                        x,
                        DateTime.UtcNow)))
                {
                    placeOrder= false;

                    decision.Reason = AppendReason(decision.Reason,
                        "Major economic event blackout window");

                    continue;
                }
                if (!placeOrder)
                {
                    _logger?.LogInformation(
                        "SKIPPED {Symbol}: rejected by final gate. " +
                        "MonteCarloScore={MonteCarloScore:F8}, " +
                        "MonteCarloProbability={Probability:P2}, " +
                        "RLReward={RLReward:F8}",
                        name,
                        decision.MonteCarloScore,
                        decision.MonteCarloProbabilityOfProfit,
                        decision.RLReward);

                    continue;
                }
                else
                {

                    
                    if (predictedReward > allowedReward && !declineSvd)
                    {
                        var orderResult =
                             await oandaClient.PlaceOrder(trade);

                        string? brokerTradeId =
                            orderResult?.orderFillTransaction?
                                .tradeOpened?
                                .tradeID;
                        string? brokerOrderId = orderResult?.orderFillTransaction?
                                            .id;

                        if (string.IsNullOrWhiteSpace(brokerTradeId))
                        {
                            decision.Reason =
                             AppendReason(
                             decision.Reason, "OANDA did not return a tradeOpened trade ID.");
                            var pending1 = _context.ChangeTracker
                                .Entries()
                                .Where(e =>
                                    e.State == EntityState.Added ||
                                    e.State == EntityState.Modified ||
                                    e.State == EntityState.Deleted)
                                .Select(e => new
                                {
                                    Entity = e.Metadata.ClrType.Name,
                                    State = e.State.ToString(),
                                    Values = e.CurrentValues.Properties.ToDictionary(
                                        p => p.Name,
                                        p => e.CurrentValues[p])
                                })
                                .ToList();

                            if (_logger != null) _logger.LogInformation("Pending database changes: {@Pending}", pending1);

                            await _context.SaveChangesAsync(
                            cancellationToken);

                            Console.WriteLine(
                            $"ORDER NOT OPENED {name}: " +
                                      "OANDA did not return a broker trade ID.");
                            if (_logger != null)
                            {
                                _logger.LogInformation($"ORDER NOT OPENED {name}: " +
                                      "OANDA did not return a broker trade ID.");
                            }
                            continue;
                        }

                        decision.BrokerTradeId =
                            brokerTradeId;

                        decision.BrokerOrderId =
                            brokerOrderId;

                        decision.OrderPlaced =
                            true;

                        decision.OrderPlacedTimeUtc =
                            DateTimeOffset.UtcNow;


                        /*
                         * Keep the local account state conservative for the
                         * remaining candidates in this same calculation pass.
                         *
                         * OpenExposure should ideally be updated from the actual
                         * units returned by the order result if that value is
                         * exposed by your OANDA response model.
                         */
                        accountState.OpenTradeCount++;

                        Console.WriteLine(
                        $"ORDER PLACED {name}: " +
                        $"BrokerTradeId={decision.BrokerTradeId}, " +
                        $"DecisionID={decision.Id}");
                        if (_logger != null)
                        {
                            _logger.LogInformation($"ORDER PLACED {name}: " +
                        $"BrokerTradeId={decision.BrokerTradeId}, " +
                        $"DecisionID={decision.Id}");
                        }
                        decision.Reason = AppendReason(decision.Reason, $"RNN Approval for Reward: {predictedReward}");
                    }else if (declineSvd)
                    {
                        decision.Reason = AppendReason(decision.Reason, "Declined By SVD");
                    }
                    else
                    {
                        decision.Reason = AppendReason(decision.Reason, $"RNN Denial for Low Reward: {predictedReward}");
                    }
                    var pending = _context.ChangeTracker
                        .Entries()
                        .Where(e =>
                            e.State == EntityState.Added ||
                            e.State == EntityState.Modified ||
                            e.State == EntityState.Deleted)
                        .Select(e => new
                        {
                            Entity = e.Metadata.ClrType.Name,
                            State = e.State.ToString(),
                            Values = e.CurrentValues.Properties.ToDictionary(
                                p => p.Name,
                                p => e.CurrentValues[p])
                        })
                        .ToList();

                    if (_logger != null)
                    {
                        _logger.LogInformation("Pending database changes: {@Pending}", pending);
                    }
                    await _context.SaveChangesAsync(
                            cancellationToken);
                }
                //catch (Exception ex)
                //{
                //    _logger?.LogError(ex, "OANDA order failed");

                //    Console.WriteLine(ex.ToString());

                //    try
                //    {
                //        decision.OrderPlaced = false;
                //        decision.Reason =
                //            AppendReason(
                //                decision.Reason,
                //                $"OANDA order failed: {ex}");

                //        await _context.SaveChangesAsync(cancellationToken);
                //    }
                //    catch (Exception saveEx)
                //    {
                //        _logger?.LogError(saveEx, "Failed saving TradeDecision");
                //        Console.WriteLine(saveEx.ToString());
                //    }
                //}
            }
                #endregion

        }
        private static double[] GetVector(string coefficientJson)
{
            return JsonSerializer.Deserialize<double[]>(
                coefficientJson)
                ?? Array.Empty<double>();
        }
        public static double Distance(
    double[] a,
    double[] b)
        {
            double sum = 0;

            for (int i = 0; i < a.Length; i++)
            {
                double d = a[i] - b[i];
                sum += d * d;
            }

            return Math.Sqrt(sum);
        }
        public static double[] BuildFeatureVector(
            string coefficientJson,
            IReadOnlyList<string> featureNames)
        {
            string featureJson = coefficientJson;
            var features =
                JsonSerializer.Deserialize<
                    Dictionary<string, object>>
                (featureJson);

            var vector = new double[featureNames.Count];

            for (int i = 0; i < featureNames.Count; i++)
            {
                if (features != null &&
                    features.TryGetValue(
                        featureNames[i],
                        out var value))
                {
                    try
                    {
                        if (value is JsonElement je)
                        {
                            if (je.ValueKind == JsonValueKind.Number)
                            {
                                vector[i] = je.GetDouble();
                            }
                            else
                            {
                                vector[i] = 0.0;
                            }
                        }
                        //vector[i] = Convert.ToDouble(value);
                    }
                    catch (Exception e) {
                        Console.WriteLine($"e.message: {e.Message}");
                    }
                }
                else
                {
                    vector[i] = 0.0;
                }
            }

            return vector;
        }
        static double[] MatrixMultiply(
    double[][] matrix,
    double[] vector)
{
    int rows = matrix.Length;
    int cols = matrix[0].Length;

    if (cols != vector.Length)
        throw new ArgumentException(
            $"Matrix columns ({cols}) must match vector size ({vector.Length})");

    var result = new double[rows];

    for (int r = 0; r < rows; r++)
    {
        double sum = 0.0;

        for (int c = 0; c < cols; c++)
        {
            sum += matrix[r][c] * vector[c];
        }

        result[r] = sum;
    }

    return result;
}
        public async Task<double[]> ComputeCoefficientsAsync(
    string instrument,
    string featureJson,
    CancellationToken cancellationToken = default)
        {
            var basis = await retriever.GetBasisAsync(
                instrument,
                cancellationToken);

            if (basis == null)
            {
                throw new InvalidOperationException(
                    $"No basis found for instrument '{instrument}'.");
            }

            var featureVector =
                BuildFeatureVector(
                    featureJson,
                    basis.FeatureNames);

            return MatrixMultiply(
                basis.Pseudoinverse,
                featureVector);
        }
        private static int GetNetUnits(Position? position)
        {
            if (position == null)
                return 0;

            var longUnits = Convert.ToInt32(position._long.units);
            var shortUnits = Convert.ToInt32(position._short.units);

            return longUnits - Math.Abs(shortUnits);
        }
        private static decimal GetPNL(Position? position)
        {
            if (position == null) return 0;
            return Convert.ToDecimal(position.unrealizedPL);
        }
        private void PopulateTradeDecision(
    TradeDecision decision,
    ConsensusTradeCandidate trade,
    TradeGateContext context,
    string? instrumentName)
        {
            decision.DecisionTimeUtc =
                context.EvaluatedAtUtc;

            decision.AccountId =
            context.AccountId;

            decision.InstrumentName =
                instrumentName;

            decision.Direction =
                trade.Direction >= 0
                   ? 1
                    : -1;

            decision.ShadowMode =
                 context.ShadowMode;

            decision.IsPracticeAccount =
                context.IsPracticeAccount;

            decision.AccountBalance =
                context.AccountBalance;

            decision.CurrentOpenExposure =
                context.CurrentOpenExposure;

            decision.OpenTradeCount =
                context.OpenTradeCount;

            decision.CurrentSpreadPips =
                context.CurrentSpreadPips;

            decision.AllowThreshold =
                context.AllowThreshold;

            decision.AverageForward =
                Convert.ToDecimal(trade.AverageForward);

            decision.MinimumConfidence =
                Convert.ToDecimal(trade.MinConfidence);

            decision.AverageConfidence =
                Convert.ToDecimal(trade.AverageConfidence);

            PopulateGranularityValues(
                decision,
                trade);
        }
        private static void PopulateGranularityValues(
           TradeDecision decision,
           ConsensusTradeCandidate trade)
        {
            if (trade.ByGranularity.TryGetValue(
                "H4",
               out var h4))
            {
                decision.H4Forward =
                    Convert.ToDecimal(h4.Forward);

                decision.H4Confidence =
                    Convert.ToDecimal(h4.Confidence);

                decision.H4Variance =
                     Convert.ToDecimal(h4.Variance);
            }

            if (trade.ByGranularity.TryGetValue(
                "H8",
               out var h8))
            {
                decision.H8Forward =
                    Convert.ToDecimal(h8.Forward);

                decision.H8Confidence =
                    Convert.ToDecimal(h8.Confidence);

                decision.H8Variance =
                     Convert.ToDecimal(h8.Variance);
            }

            if (trade.ByGranularity.TryGetValue(
                "D",
                out var daily))
            {
                decision.DForward =
                    Convert.ToDecimal(daily.Forward);

                decision.DConfidence =
                    Convert.ToDecimal(daily.Confidence);

                decision.DVariance =
           Convert.ToDecimal(daily.Variance);
            }
        }

        private TradeDecision CreateFailedTradeDecision(
       ConsensusTradeCandidate trade,
        TradingAccountState accountState,
        string instrumentName,
        string reason)
        {

            var decision =
           new TradeDecision
           {
               Id =
                        Guid.NewGuid(),

               DecisionTimeUtc =
                        DateTimeOffset.UtcNow,

               AccountId =
                        accountState.AccountId,

               InstrumentName =
                        instrumentName,

               Direction =
                   trade.Direction >= 0
                           ? 1
                            : -1,

               Action =
               TradeGateAction.Skip,

               AllowProbability =
               0.0f,

               AllowThreshold =
                        0.65f,

               ModelVersion =
                 "error",

               Reason =
                        reason,

               ShadowMode =
                        true,

               IsPracticeAccount =
                        accountState.IsPractice,

               AccountBalance =
                        accountState.Balance,

               CurrentOpenExposure =
                        accountState.OpenExposure,

               OpenTradeCount =
                        accountState.OpenTradeCount,

               AverageForward =
                        Convert.ToDecimal(trade.AverageForward),

               MinimumConfidence =
        Convert.ToDecimal(trade.MinConfidence),

               AverageConfidence =
                        Convert.ToDecimal(trade.AverageConfidence),

               FeatureSchemaVersion =
                        "trade-gate-v2-montecarlo",

               OrderPlaced =
        false,

               RewardCalculated =
                       false,
           };

            PopulateGranularityValues(
                decision, trade);
            var features = new
            {
                Symbol = instrumentName,
                decision.FeatureSchemaVersion,
                decision.AccountBalance,
                decision.AverageConfidence,
                decision.AverageForward,
                decision.MinimumConfidence,
                decision.CurrentOpenExposure,
                decision.CurrentSpreadPips,
                decision.H4Confidence,
                decision.H4Forward,
                decision.H4Variance,
                decision.H8Confidence,
                decision.H8Forward,
                decision.H8Variance,
                decision.DConfidence,
                decision.DForward,
                decision.DVariance,
                GeneratedUtc = DateTime.UtcNow
            };
            decision.FeatureJson = JsonSerializer.Serialize(features);
            return decision;
        }

        private static string AppendReason(
            string? existingReason,
            string additionalReason)
        {
            if (string.IsNullOrWhiteSpace(existingReason))
            {
                return additionalReason;
            }

            return $"{existingReason} {additionalReason}";
        }
        public async Task ActOnTradeCandidates(CancellationToken cancellationToken = default)
        {
            var account = await oandaClient.GetAccount();
            var instrumentRoot = await oandaClient.GetInstruments();
            List<Data.Oanda.Objects.Instrument> instruments =
                instrumentRoot.instruments
                    .OrderBy(ins => ins.name)
                    .ToList();
            instrumentNames = instruments.Select(ins => ins.name).ToArray();
            allowedInstruments = instruments.Select(ins => ins.name).Where(ins =>
            !ins.Contains("CNH")
            && !ins.Contains("DKK")
            && !ins.Contains("PLN")
            && !ins.Contains("CZK")
            && !ins.Contains("HKD")
            && !ins.Contains("TRY")).ToArray();
            bool[] tradeMask =
                instruments
                    .Select(ins => allowedInstruments.Contains(ins.name))
                    .ToArray();

            List<TradeCandidate> h4Candidates = BuildTradeCandidates(projections["H4"], tradeMask);
            List<TradeCandidate> h8Candidates = BuildTradeCandidates(projections["H8"], tradeMask);
            List<TradeCandidate> dCandidates = BuildTradeCandidates(projections["D"], tradeMask);

            var byGranularity = new Dictionary<string, List<TradeCandidate>>
            {
                ["H4"] = h4Candidates,
                ["H8"] = h8Candidates,
                ["D"] = dCandidates
            };

            string[] requiredGranularities =
            [
                "H4",
                "H8",
                "D"
            ];

            List<ConsensusTradeCandidate> consensus =
                FindTopConsensusTrades(
                    byGranularity,
                    requiredGranularities,
                    perGranularityPoolSize: 30,
                    finalCount: 10,
                    instrumentNames: instrumentNames);
            var learnedContainer = await LearnedContainerClass.ReturnLearnedContainer(cosmosClient);
            var accountState =
    await oandaClient.GetTradingAccountState();
            var declineSvd = false;
            foreach (var trade in consensus)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool monteCarloApproved;
                string direction =
          trade.Direction >= 0
                       ? "LONG"
                       : "SHORT";

                string name = trade.InstrumentName;


                decimal currentSpreadPips;

                try
                {
                    currentSpreadPips =
                        await oandaClient.GetCurrentSpreadPips(name);
                }
                catch (Exception ex)
                {
                    /*
                     * A missing spread is a risk-data failure.
                     * Record a failed decision and do not place the order.
                     */
                    var spreadFailureDecision =
                        CreateFailedTradeDecision(
                           trade,
                            accountState,
                            name,
                    $"Unable to retrieve spread: {ex.Message}");

                    _context.TradeDecisions.Add(
              spreadFailureDecision);

                    await _context.SaveChangesAsync(
                        cancellationToken);

                    Console.WriteLine(
                        $"RL-GATE ERROR {name}: " +
                    spreadFailureDecision.Reason);
                    continue;
                }

                var gateContext =
                         new TradeGateContext
                         {
                             EvaluatedAtUtc = DateTimeOffset.UtcNow,
                             AccountId = accountState.AccountId,

                             IsPracticeAccount =
                    accountState.IsPractice,

                             AccountBalance =
                    accountState.Balance,

                             CurrentOpenExposure =
                    accountState.OpenExposure,

                             OpenTradeCount =
                    accountState.OpenTradeCount,

                             CurrentSpreadPips =
                    currentSpreadPips,

                             AllowThreshold =
                    0.65f,

                             /*
                              * Keep true while collecting training data.
                              */
                             ShadowMode =
                    false
                         }
            ;

                TradeDecision decision;

                try
                {
                    var monteCarlo = await GetOrCreateMonteCarloAsync(
    trade,
    cancellationToken);

                    /*
                     * Do not use a prediction generated before this candidate
                     * projection. Otherwise the gate could combine a new candidate
                     * with an old simulated market state.
                     */
                    if (monteCarlo.CreatedUtc < trade.GeneratedUtc)
                    {
                        throw new InvalidOperationException(
                            $"Monte Carlo prediction for {trade.InstrumentName} " +
                            $"is stale. Prediction: {monteCarlo.CreatedUtc:O}; " +
                            $"candidate: {trade.GeneratedUtc:O}.");
                    }
                    #region state
                    bool isLong = trade.Direction > 0;

                    double directionalReturn =
                        isLong
                            ? monteCarlo.MeanReturn
                            : -monteCarlo.MeanReturn;

                    double directionalProbProfit =
                        isLong
                            ? monteCarlo.ProbProfit
                            : 1.0 - monteCarlo.ProbProfit;



                    decimal originalAverageForward = trade.AverageForward;
                    decimal originalScore = trade.AverageConfidence;

                    double directionalMeanReturn =
                        trade.Direction > 0
                            ? monteCarlo.MeanReturn
                            : -monteCarlo.MeanReturn;

                    double directionalProbability =
                        trade.Direction > 0
                            ? monteCarlo.ProbProfit
                            : 1.0 - monteCarlo.ProbProfit;

                    double directionalWorstDecile =
                        trade.Direction > 0
                            ? monteCarlo.WorstDecile
                            : -monteCarlo.BestDecile;

                    double directionalBestDecile =
                        trade.Direction > 0
                            ? monteCarlo.BestDecile
                            : -monteCarlo.WorstDecile;

                    double directionalRiskAdjustedReturn =
                        directionalMeanReturn /
                        Math.Max(monteCarlo.StdDev, 0.000001);

                    double monteCarloScore =
                        directionalRiskAdjustedReturn *
                        directionalProbability *
                        Math.Max((double)trade.AverageConfidence, 0.05);
                    monteCarloApproved =
                         directionalProbProfit >= 0.55 &&
                         directionalRiskAdjustedReturn > 0 &&
                         directionalWorstDecile > -0.005;
                    /*
                     * Replace the original KNN candidate return and ranking score.
                     *
                     * Keep the original values in separate properties if you want
                     * them for future training and diagnostics.
                     */
                    trade.OriginalAverageForward = trade.AverageForward;
                    trade.OriginalScore = trade.Score;

                    trade.AverageForward = (decimal)directionalReturn;
                    trade.Score = (decimal)monteCarloScore;

                    /*
                     * If gateContext duplicates these values, update it too.
                     * Otherwise EvaluateAsync could still use the original values.
                     */
                    gateContext.AverageForward = (decimal)directionalReturn;
                    gateContext.CandidateScore = (decimal)monteCarloScore;
                    gateContext.ProbabilityOfProfit = (decimal)directionalProbProfit;
                    gateContext.WorstDecile = (decimal)directionalWorstDecile;
                    gateContext.BestDecile = (decimal)directionalBestDecile;
                    gateContext.ReturnStdDev = (decimal)monteCarlo.StdDev;

                    decision =
                        await _tradeGate.EvaluateAsync(
                            trade,
                            gateContext,
                            cancellationToken);

                    /*
                     * Copy the Monte Carlo state into the persisted TradeDecision.
                     */
                    decision.MonteCarloMeanReturn =
                        (decimal)directionalReturn;

                    decision.MonteCarloStdDev =
                        (decimal)monteCarlo.StdDev;

                    decision.MonteCarloProbabilityOfProfit =
                        (decimal)directionalProbProfit;

                    decision.MonteCarloWorstDecile =
                        (decimal)directionalWorstDecile;

                    decision.MonteCarloMedianReturn =
                        (decimal)(
                            isLong
                                ? monteCarlo.MedianReturn
                                : -monteCarlo.MedianReturn);

                    decision.MonteCarloBestDecile =
                        (decimal)directionalBestDecile;

                    decision.MonteCarloRiskAdjustedReturn =
                        (decimal)directionalRiskAdjustedReturn;

                    decision.MonteCarloScore =
                        (decimal)monteCarloScore;

                    decision.MonteCarloSimulationCount =
                        monteCarlo.SimulationCount;

                    decision.MonteCarloSteps =
                        monteCarlo.Steps;

                    decision.MonteCarloCalculatedUtc =
                        monteCarlo.CreatedUtc;
                    #endregion
                    #region features
                    var features = new
                    {
                        accountState.NetAssetValue,
                        accountState.Balance,
                        accountState.MarginAvailable,
                        accountState.MarginUsed,
                        accountState.OpenExposure,
                        accountState.OpenTradeCount,
                        accountState.UnrealizedPnL,

                        // Keep your existing position PNL/unit features here.
                        AUD_CADPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_CAD")),
                        AUD_CADUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_CAD")),
                        AUD_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_CHF")),
                        AUD_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_CHF")),
                        AUD_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_JPY")),
                        AUD_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_JPY")),
                        AUD_NZDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_NZD")),
                        AUD_NZDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_NZD")),
                        AUD_SGDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_SGD")),
                        AUD_SGDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_SGD")),
                        AUD_USDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_USD")),
                        AUD_USDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "AUD_USD")),
                        CAD_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "CAD_CHF")),
                        CAD_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "CAD_CHF")),
                        CAD_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "CAD_JPY")),
                        CAD_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "CAD_JPY")),
                        CAD_SGDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "CAD_SGD")),
                        CAD_SGDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "CAD_SGD")),
                        CHF_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "CHF_JPY")),
                        CHF_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "CHF_JPY")),
                        CHF_ZARPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "CHF_ZAR")),
                        CHF_ZARUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "CHF_ZAR")),
                        EUR_AUDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_AUD")),
                        EUR_AUDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_AUD")),
                        EUR_CADPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_CAD")),
                        EUR_CADUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_CAD")),
                        EUR_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_CHF")),
                        EUR_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_CHF")),
                        EUR_GBPPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_GBP")),
                        EUR_GBPUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_GBP")),
                        EUR_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_JPY")),
                        EUR_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_JPY")),
                        EUR_NZDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_NZD")),
                        EUR_NZDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_NZD")),
                        EUR_SEKPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_SEK")),
                        EUR_SEKUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_SEK")),
                        EUR_SGDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_SGD")),
                        EUR_SGDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_SGD")),
                        EUR_TRYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_TRY")),
                        EUR_TRYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_TRY")),
                        EUR_USDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_USD")),
                        EUR_USDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_USD")),
                        EUR_ZARPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_ZAR")),
                        EUR_ZARUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "EUR_ZAR")),
                        GBP_AUDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_AUD")),
                        GBP_AUDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_AUD")),
                        GBP_CADPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_CAD")),
                        GBP_CADUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_CAD")),
                        GBP_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_CHF")),
                        GBP_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_CHF")),
                        GBP_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_JPY")),
                        GBP_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_JPY")),
                        GBP_NZDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_NZD")),
                        GBP_NZDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_NZD")),
                        GBP_SGDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_SGD")),
                        GBP_SGDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_SGD")),
                        GBP_USDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_USD")),
                        GBP_USDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_USD")),
                        GBP_ZARPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_ZAR")),
                        GBP_ZARUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "GBP_ZAR")),
                        NZD_CADPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_CAD")),
                        NZD_CADUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_CAD")),
                        NZD_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_CHF")),
                        NZD_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_CHF")),
                        NZD_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_JPY")),
                        NZD_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_JPY")),
                        NZD_SGDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_SGD")),
                        NZD_SGDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_SGD")),
                        NZD_USDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_USD")),
                        NZD_USDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "NZD_USD")),
                        SGD_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "SGD_CHF")),
                        SGD_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "SGD_CHF")),
                        SGD_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "SGD_JPY")),
                        SGD_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "SGD_JPY")),
                        TRY_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "TRY_JPY")),
                        TRY_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "TRY_JPY")),
                        USD_CADPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_CAD")),
                        USD_CADUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_CAD")),
                        USD_CHFPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_CHF")),
                        USD_CHFUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_CHF")),
                        USD_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_JPY")),
                        USD_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_JPY")),
                        USD_MXNPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_MXN")),
                        USD_MXNUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_MXN")),
                        USD_SEKPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_SEK")),
                        USD_SEKUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_SEK")),
                        USD_SGDPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_SGD")),
                        USD_SGDUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_SGD")),
                        USD_THBPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_THB")),
                        USD_THBUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_THB")),
                        USD_TRYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_TRY")),
                        USD_TRYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_TRY")),
                        USD_ZARPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "USD_ZAR")),
                        USD_ZARUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "USD_ZAR")),
                        ZAR_JPYPNL = GetPNL(account.positions.FirstOrDefault(pos => pos.instrument == "ZAR_JPY")),
                        ZAR_JPYUnits = GetNetUnits(account.positions.FirstOrDefault(pos => pos.instrument == "ZAR_JPY")),
                        Symbol = trade.InstrumentName,

                        decision.FeatureSchemaVersion,
                        decision.AccountBalance,

                        /*
                         * Preserve KNN confidence and variance as context.
                         * You are replacing its return, not necessarily discarding
                         * all information from the projection model.
                         */
                        decision.AverageConfidence,
                        decision.MinimumConfidence,
                        decision.CurrentOpenExposure,
                        decision.CurrentSpreadPips,

                        decision.H4Confidence,
                        decision.H4Variance,

                        decision.H8Confidence,
                        decision.H8Variance,

                        decision.DConfidence,
                        decision.DVariance,

                        GeneratedUtc = DateTime.UtcNow,

                        /*
                         * Monte Carlo now supplies return and score.
                         */
                        AverageForward = directionalReturn,
                        CandidateScore = monteCarloScore,

                        MonteCarloMeanReturn =
                            directionalReturn,

                        MonteCarloStdDev =
                            monteCarlo.StdDev,

                        MonteCarloProbabilityOfProfit =
                            directionalProbProfit,

                        MonteCarloWorstDecile =
                            directionalWorstDecile,

                        MonteCarloMedianReturn =
                            isLong
                                ? monteCarlo.MedianReturn
                                : -monteCarlo.MedianReturn,

                        MonteCarloBestDecile =
                            directionalBestDecile,

                        MonteCarloRiskAdjustedReturn =
                            directionalRiskAdjustedReturn,

                        MonteCarloReturnSpread =
                            directionalBestDecile -
                            directionalWorstDecile,

                        MonteCarloSimulationCount =
                            monteCarlo.SimulationCount,

                        MonteCarloSteps =
                            monteCarlo.Steps,

                        MonteCarloCalculatedUtc =
                            monteCarlo.CreatedUtc,
                        TradeDirection = trade.Direction,

                        // Raw market expectation
                        AverageForwardRaw =
    monteCarlo.MeanReturn,

                        MonteCarloMeanReturnRaw =
    monteCarlo.MeanReturn,

                        // Trade-adjusted expectation
                        AverageForwardDirectional =
    directionalReturn,

                        MonteCarloMeanReturnDirectional =
    directionalReturn,


                        // Agreement between forecast and trade direction
                        SignalAgreement =
    Math.Sign((double)monteCarlo.MeanReturn) ==
    Math.Sign(trade.Direction),

                        h4CandleDistance = projections["H4"].Stats[0],
                        h4DeltaDistance = projections["H4"].Stats[1],
                        h4OverAverageDistance = projections["H4"].Stats[2],
                        h4MaDelta5Distance = projections["H4"].Stats[3],
                        h4MaDelta10Distance = projections["H4"].Stats[4],
                        h4MaOverAverage5Distance = projections["H4"].Stats[5],
                        h4MaOverAverage10Distance = projections["H4"].Stats[6],
                        h4CorrelationDistance = projections["H4"].Stats[7],

                        h8CandleDistance = projections["H8"].Stats[0],
                        h8DeltaDistance = projections["H8"].Stats[1],
                        h8OverAverageDistance = projections["H8"].Stats[2],
                        h8MaDelta5Distance = projections["H8"].Stats[3],
                        h8MaDelta10Distance = projections["H8"].Stats[4],
                        h8MaOverAverage5Distance = projections["H8"].Stats[5],
                        h8MaOverAverage10Distance = projections["H8"].Stats[6],
                        h8CorrelationDistance = projections["H8"].Stats[7],

                        dCandleDistance = projections["D"].Stats[0],
                        dDeltaDistance = projections["D"].Stats[1],
                        dOverAverageDistance = projections["D"].Stats[2],
                        dMaDelta5Distance = projections["D"].Stats[3],
                        dMaDelta10Distance = projections["D"].Stats[4],
                        dMaOverAverage5Distance = projections["D"].Stats[5],
                        dMaOverAverage10Distance = projections["D"].Stats[6],
                        dCorrelationDistance = projections["D"].Stats[7]


                    };
                    

                    #endregion
                    decision.FeatureJson =
                        JsonSerializer.Serialize(features);
                    #region SVD
                    decision.FeatureJson =
                        JsonSerializer.Serialize(features);
                    var basis = await retriever
                        .GetBasisAsync(decision.InstrumentName);

                    Console.WriteLine($"basis null? {basis == null}");
                    // Existing features
                    var features_ =
                        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                            decision.FeatureJson)
                        ?? new Dictionary<string, JsonElement>();

                    // Build x
                    var x = BuildFeatureVector(
                        decision.FeatureJson,
                        basis.FeatureNames);

                    // Compute projection
                    double[] VkTx = ProjectOntoSubspace(
                        learnedContainer.RightSingularVectors,
                        x);

                    // Add coefficient coordinates
                    for (int i = 0; i < 41; i++)
                    {
                        features_[$"Coeff_{i + 1:D2}"] =
                            JsonSerializer.SerializeToElement(VkTx[i]);
                    }

                    features_["SVD_Energy"] =
                        JsonSerializer.SerializeToElement(
                            VkTx.Sum(v => v * v));

                    features_["SVD_MaxAbs"] =
                        JsonSerializer.SerializeToElement(
                            VkTx.Max(v => Math.Abs(v)));

                    features_["SVD_Mean"] =
                        JsonSerializer.SerializeToElement(
                            VkTx.Average());

                    features_["SVD_StdDev"] =
                        JsonSerializer.SerializeToElement(
                            Math.Sqrt(VkTx.Select(v => v * v).Average()));// Save back
                    decision.FeatureJson =
                        JsonSerializer.Serialize(features_);

                    var xAnyHigh = VkTx.Any(y => Math.Abs(y) > 599.001);
                    double energy =
    VkTx.Sum(v => v * v);
                    //var xLowAny = VkTx.Any(y => Math.Abs(y) <= 10.2);
                    var vktxAvg = VkTx.Average();
                    bool tiltedUp = (VkTx.OrderByDescending(i => i).Take(5).Average() + VkTx.Order().Take(5).Average()) / 2 > vktxAvg;

                    var projection = ReconstructFromSubspace(
learnedContainer.RightSingularVectors,
VkTx);
                    var threshold = 1.5;
                    var residual =
                        projection.Zip(x, (p, xv) => (xv - p) * (xv - p))
                                  .Sum();

                    double residualNorm = Math.Sqrt(residual);
                    double positiveTail =
    VkTx.OrderByDescending(Math.Abs)
        .Take(5)
        .Average(Math.Abs);
                    if (residualNorm > threshold)
                    {
                        declineSvd = false;

                        decision.Reason = AppendReason(decision.Reason, $"Singular avg {vktxAvg:F1}, high {VkTx.Max(Math.Abs):F1}, low {VkTx.Min(Math.Abs):F1}");
                        //continue;
                    }
                    else
                    {
                        Console.WriteLine("SVD Acceptable response");
                    }
                    //if (factorRank.Any(ranking => 
                    //{
                    //    return ranking.SingularValue < 094;


                    //})
                    //    )
                    //{ if (factorRank.All(ranking1 => {
                    //        return ranking1.SingularValue > 11.1 && xAnyHigh;
                    //    }))
                    //    {
                    //        AppendReason(decision.Reason,
                    //        String.Format("ranking1.MaxSingularvalue= {0}", x.Max()));
                    //            continue;
                    //            }}
                    //else
                    //{

                    //    AppendReason(decision.Reason, String.Format(factorRank.Any(ranking => ranking.SingularValue > 118.19).ToString()));
                    //    continue;
                    //}

                    Console.WriteLine(
    $"Pseudo Rows: {basis.Pseudoinverse?.Length}");

                    Console.WriteLine(
                        $"Pseudo Cols: {basis.Pseudoinverse?[0]?.Length}");

                    Console.WriteLine(
                        $"Vector Length: {x.Length}");


                    /*            var coefficients =
                              await retriever.ComputeCoefficientsAsync(
                                  decision.InstrumentName, //decision.InstrumentName,
                                  x,
                                  cancellationToken);
                                var vector = new CoefficientVector
                                {
                                    DecisionId = decision.Id,
                                    Instrument = decision.InstrumentName,
                                    BasisVersion = basis.ModelVersion,
                                    BasisRank = basis.Rank,
                                    FeatureSchemaHash = basis.FeatureSchemaHash,
                                    CreatedUtc = DateTime.UtcNow,
                                    CoefficientJson = JsonSerializer.Serialize(coefficients)
                                };

                                //_context.CoefficientVectors.Add(vector);
                                //await _context.SaveChangesAsync();
                                /*var rows = await _context.CoefficientVectors
            .Where(x => x.Reward != null)
            .ToListAsync();
                                            var nearest =
                             rows
                                 .OrderBy(y => Distance(
                                     x,
                                     GetVector(y.CoefficientJson)))
                                 .First();
                                            Console.WriteLine(
                $"Features: {x.Length}");

                                Console.WriteLine(
                                    $"PseudoInverse Rows: {basis.Pseudoinverse.Length}");

                                Console.WriteLine(
                                    $"PseudoInverse Cols: {basis.Pseudoinverse[0].Length}");

                                */


                    #endregion
                    PopulateTradeDecision(
                        decision,
                        trade,
                        gateContext,
                        name);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"PRE-RL-GATE ERROR {name}: {ex.Message}");

                    decision =
                        CreateFailedTradeDecision(
                            trade,
                            accountState,
                            name,
                            $"Trade gate evaluation failed: {ex.Message}");

                    decision.CurrentSpreadPips =
                        currentSpreadPips;

                    decision.AllowThreshold =
                        gateContext.AllowThreshold;

                    decision.ShadowMode =
                        gateContext.ShadowMode;
                    monteCarloApproved = false;
                }

                /*
                 * Save the initial state/action before attempting the order.
                 * This records skipped decisions and also preserves the
                 * decision if the OANDA order later fails.
                 */
                _context.TradeDecisions.Add(decision);

                await _context.SaveChangesAsync(cancellationToken);

                if (!decision.ShouldPlaceTrade && !decision.ShadowMode)
                {
                    //LogSkipped(name, "Rejected by trade gate.");
                    continue;
                }
                #region placeOrder
                double predictedReward;
                bool placeOrder;
                try
                {
                    predictedReward =
                        (double)_rewardModelService.Predict(
                            decision.FeatureJson);

                    decision.RLReward =
                        (decimal)Math.Round(
                            predictedReward,
                            7,
                            MidpointRounding.AwayFromZero);

                    bool rewardApproved =
                        predictedReward > allowedReward;
                    bool FinalTradeApproved =
                    decision.ShouldPlaceTrade && RewardGateApproved;

                    placeOrder = FinalTradeApproved && decision.ShouldPlaceTrade;

                    decision.Reason =
                        AppendReason(
                            decision.Reason,
                            rewardApproved
                                ? $"Reward gate approved: {predictedReward:F8}."
                                : $"Reward gate rejected: {predictedReward:F8}.");

                    await _context.SaveChangesAsync(
                        cancellationToken);
                    if (!monteCarloApproved)
                    {
                        decision.Reason = AppendReason(decision.Reason, $"Monte Carlo Denial for : profit proability {decision.MonteCarloProbabilityOfProfit}, wost decile {decision.MonteCarloWorstDecile}");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    decision.Reason =
                        AppendReason(
                            decision.Reason,
                            $"Reward inference failed: {ex.Message}");

                    await _context.SaveChangesAsync(
                        cancellationToken);

                    _logger?.LogError(
                        ex,
                        "Reward inference failed for {Symbol}",
                        name);

                    continue;
                }

                if (decision.ShadowMode)
                {
                    if (!decision.IsPracticeAccount)
                    {
                        Console.WriteLine(
                            $"BLOCKED {name}: shadow-mode orders " +
                            "are restricted to practice accounts.");

                        continue;
                    }

                    placeOrder = true;
                }
                else
                {
                    placeOrder =
                        decision.ShouldPlaceTrade;
                }

                if (!placeOrder)
                {
                    _logger?.LogInformation(
                        "SKIPPED {Symbol}: rejected by final gate. " +
                        "MonteCarloScore={MonteCarloScore:F8}, " +
                        "MonteCarloProbability={Probability:P2}, " +
                        "RLReward={RLReward:F8}",
                        name,
                        decision.MonteCarloScore,
                        decision.MonteCarloProbabilityOfProfit,
                        decision.RLReward);

                    continue;
                }
                else
                {
                    if (predictedReward > allowedReward)
                    {
                        var orderResult =
                             await oandaClient.PlaceOrder(trade);

                        string? brokerTradeId =
                            orderResult?.orderFillTransaction?
                                .tradeOpened?
                                .tradeID;
                        string? brokerOrderId = orderResult?.orderFillTransaction?
                                            .id;

                        if (string.IsNullOrWhiteSpace(brokerTradeId))
                        {
                            decision.Reason =
                             AppendReason(
                             decision.Reason, "OANDA did not return a tradeOpened trade ID.");
                            var pending1 = _context.ChangeTracker
                                .Entries()
                                .Where(e =>
                                    e.State == EntityState.Added ||
                                    e.State == EntityState.Modified ||
                                    e.State == EntityState.Deleted)
                                .Select(e => new
                                {
                                    Entity = e.Metadata.ClrType.Name,
                                    State = e.State.ToString(),
                                    Values = e.CurrentValues.Properties.ToDictionary(
                                        p => p.Name,
                                        p => e.CurrentValues[p])
                                })
                                .ToList();

                            if (_logger != null) _logger.LogInformation("Pending database changes: {@Pending}", pending1);

                            await _context.SaveChangesAsync(
                            cancellationToken);

                            Console.WriteLine(
                            $"ORDER NOT OPENED {name}: " +
                                      "OANDA did not return a broker trade ID.");
                            if (_logger != null)
                            {
                                _logger.LogInformation($"ORDER NOT OPENED {name}: " +
                                      "OANDA did not return a broker trade ID.");
                            }
                            continue;
                        }

                        decision.BrokerTradeId =
                            brokerTradeId;

                        decision.BrokerOrderId =
                            brokerOrderId;

                        decision.OrderPlaced =
                            true;

                        decision.OrderPlacedTimeUtc =
                            DateTimeOffset.UtcNow;


                        /*
                         * Keep the local account state conservative for the
                         * remaining candidates in this same calculation pass.
                         *
                         * OpenExposure should ideally be updated from the actual
                         * units returned by the order result if that value is
                         * exposed by your OANDA response model.
                         */
                        accountState.OpenTradeCount++;

                        Console.WriteLine(
                        $"ORDER PLACED {name}: " +
                        $"BrokerTradeId={decision.BrokerTradeId}, " +
                        $"DecisionID={decision.Id}");
                        if (_logger != null)
                        {
                            _logger.LogInformation($"ORDER PLACED {name}: " +
                        $"BrokerTradeId={decision.BrokerTradeId}, " +
                        $"DecisionID={decision.Id}");
                        }
                        decision.Reason = AppendReason(decision.Reason, $"RNN Approval for Reward: {predictedReward}");
                    }
                    else
                    {
                        decision.Reason = AppendReason(decision.Reason, $"RNN Denial for Low Reward: {predictedReward}");
                    }
                    var pending = _context.ChangeTracker
                        .Entries()
                        .Where(e =>
                            e.State == EntityState.Added ||
                            e.State == EntityState.Modified ||
                            e.State == EntityState.Deleted)
                        .Select(e => new
                        {
                            Entity = e.Metadata.ClrType.Name,
                            State = e.State.ToString(),
                            Values = e.CurrentValues.Properties.ToDictionary(
                                p => p.Name,
                                p => e.CurrentValues[p])
                        })
                        .ToList();

                    if (_logger != null)
                    {
                        _logger.LogInformation("Pending database changes: {@Pending}", pending);
                    }
                    await _context.SaveChangesAsync(
                            cancellationToken);
                }
                //catch (Exception ex)
                //{
                //    _logger?.LogError(ex, "OANDA order failed");

                //    Console.WriteLine(ex.ToString());

                //    try
                //    {
                //        decision.OrderPlaced = false;
                //        decision.Reason =
                //            AppendReason(
                //                decision.Reason,
                //                $"OANDA order failed: {ex}");

                //        await _context.SaveChangesAsync(cancellationToken);
                //    }
                //    catch (Exception saveEx)
                //    {
                //        _logger?.LogError(saveEx, "Failed saving TradeDecision");
                //        Console.WriteLine(saveEx.ToString());
                //    }
                //}
            }
                #endregion
        }
        public static float CalculateReward(
    decimal realizedProfitLoss,
    decimal transactionCost,
    decimal maximumAdverseExcursion,
    decimal riskAmount)
        {
            if (riskAmount <= 0)
                return 0.0f;

            decimal netProfit =
                realizedProfitLoss - transactionCost;

            decimal normalizedProfit =
                netProfit / riskAmount;

            decimal drawdownPenalty =
                Math.Abs(
                    Math.Min(
                        maximumAdverseExcursion,
                        0m)) / riskAmount;

            decimal reward =
                normalizedProfit -
                (0.25m * drawdownPenalty);

            return (float)Math.Clamp(
                reward,
                -3.0m,
                3.0m);
        }


        #endregion
        public static double CalculateNewsRisk(
    List<EconomicEvent> events,
    DateTime nowUtc)
        {
            double risk = 0;

            foreach (var e in events)
            {
                var minutes =
                    (e.EventUtc - nowUtc).TotalMinutes;

                if (minutes < 0)
                    continue;

                switch (e.Impact)
                {
                    case 3:
                        if (minutes <= 15)
                            risk = Math.Max(risk, 1.0);
                        else if (minutes <= 30)
                            risk = Math.Max(risk, 0.75);
                        else if (minutes <= 60)
                            risk = Math.Max(risk, 0.50);
                        break;

                    case 2:
                        if (minutes <= 30)
                            risk = Math.Max(risk, 0.40);
                        else if (minutes <= 60)
                            risk = Math.Max(risk, 0.20);
                        break;
                }
            }

            return risk;
        }
        public static bool IsInNewsBlackout(
    EconomicEvent evt,
    DateTime nowUtc)
        {
            return evt.Impact >= 3
                   && nowUtc >= evt.EventUtc.AddMinutes(-15)
                   && nowUtc <= evt.EventUtc.AddMinutes(15);
        }
        /// <summary>
        /// Computes Vk^T * x, projecting a feature vector onto the top-k singular-vector subspace.
        /// </summary>
        /// <param name="vk">Vk, shape (d x k) — d = feature dim, k = retained factors. Columns are the retained right singular vectors.</param>
        /// <param name="x">Feature vector, length d.</param>
        /// <returns>Projected vector, length k.</returns>
        public static double[] ProjectOntoSubspace(List<List<double>> vk, double[] x)
        {
            int d = vk.Count();
            int k = vk[0].Count();

            if (x.Length != d)
                throw new ArgumentException($"x has length {x.Length}, expected {d} to match Vk's row count.");

            var result = new double[k];

            for (int j = 0; j < k; j++)
            {
                double sum = 0.0;
                for (int i = 0; i < d; i++)
                {
                    sum += vk[i][j] * x[i];
                }
                result[j] = sum;
            }

            return result;
        }
        public static double[] ReconstructFromSubspace(
    List<List<double>> Vk,
    double[] VkTx)
        {
            int rows = Vk.Count(); // 158 features
            int cols = Vk[0].Count(); // k = 41

            var reconstructed = new double[rows];

            for (int i = 0; i < rows; i++)
            {
                double sum = 0;

                for (int j = 0; j < cols; j++)
                {
                    sum += Vk[i][j] * VkTx[j];
                }

                reconstructed[i] = sum;
            }

            return reconstructed;
        }
        public static Dictionary<string, double> ExtractNumericFeatures(
    string featureJson)
        {
            var raw =
                JsonSerializer.Deserialize<
                    Dictionary<string, JsonElement>>(featureJson);

            var numeric =
                new Dictionary<string, double>();

            foreach (var kvp in raw)
            {
                if (kvp.Value.ValueKind ==
                    JsonValueKind.Number)
                {
                    numeric[kvp.Key] =
                        kvp.Value.GetDouble();
                }
            }

            return numeric;
        }
    }
}