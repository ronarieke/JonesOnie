using Azure.Storage.Blobs;
using FibonacciTrade.Data.DataCollection.Objects;
using FibonacciTrade.Logic;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FibonacciTrade.ALgorithm
{
    public class BlobCollector
    {

        private const int WindowSize = 20;
        private const int InstrumentCount = 68;
        private const int FeaturesPerInstrument = 5;
        private const int K = 50;
        private readonly BlobContainerClient blobContainerClient;
        public BlobCollector()
        {
            string blobConnStr = "DefaultEndpointsProtocol=https;AccountName=fibonaccitrade;AccountKey=o4GwvkWMh4+AiokV7cd6VF+6qHF9mu+Qi2HEj9DIy1NSNVgIDq4pZpUxvN7LoznSuX67CHLwRotm+AStUb5M8A==;EndpointSuffix=core.windows.net";
            string blobcontainer = "historical-data";

            blobContainerClient = new BlobContainerClient(
                blobConnStr,
                blobcontainer);

        }
        public async Task<CosmosSlice> LoadSliceAsync(string uri)
        {
            BlobClient blobClient = blobContainerClient.GetBlobClient(uri);

            using MemoryStream ms = new MemoryStream();

            await blobClient.DownloadToAsync(ms);

            ms.Position = 0;

            CosmosSlice? slice =
                await JsonSerializer.DeserializeAsync<CosmosSlice>(ms);

            if (slice == null)
                throw new InvalidOperationException($"Failed to deserialize {uri}");

            return slice;
        }
        public async Task<CosmosSlice[]> LoadSlicesAsync(
    IEnumerable<CosmosPointer> pointers)
        {
            List<Task<CosmosSlice>> tasks = pointers
                .Select(p => LoadSliceAsync(p.uri))
                .ToList();

            return await Task.WhenAll(tasks);
        }
        public async IAsyncEnumerable<CosmosSlice> EnumerateSlicesAsync(
    IEnumerable<CosmosPointer> pointers)
        {
            foreach (CosmosPointer pointer in pointers)
            {
                yield return await LoadSliceAsync(pointer.uri);
            }
        }

        public async Task<ForwardProjection> ComparisonAlgorithm(
    IEnumerable<CosmosPointer> pointers,
    CosmosSlice currentSlice)
        {
            List<Neighbor> winners = new();

            await foreach (CosmosSlice slice in EnumerateSlicesAsync(pointers))
            {
                (decimal distance, decimal[] futureMove, decimal[] stats) =
                    CalculateDistance(currentSlice, slice);

                winners.Add(new Neighbor
                {
                    Distance = distance,
                    Id = slice.id,
                    FutureMove = futureMove, 
                    Stats = stats
                });

                winners = winners
                    .OrderBy(n => n.Distance)
                    .Take(K)
                    .ToList();
            }

            return CompileWeightedAverage(winners);
        }
        decimal candleWeight = 1.0m * 0.888m;
        decimal deltaWeight = 2.0m;
        decimal overAverageWeight = 1.0m * 0.964m;
        decimal MADelta5Weight = 10.0m * 1.123m;
        decimal MADelta10Weight = 10.0m * 1.101m;
        decimal MAOverAverage5Weight = 1.0m;
        decimal MAOverAverage10Weight = 1.0m;
        decimal CorrelationWeight = 0.5m * 0.678m;
        private static void ValidateSameShape(decimal[][] left, decimal[][] right)
        {
            if (left == null || right == null)
                throw new ArgumentNullException("Matrix arguments cannot be null.");

            if (left.Length != right.Length)
                throw new InvalidOperationException(
                    $"Matrix row mismatch. Left={left.Length}, Right={right.Length}");

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i].Length != right[i].Length)
                {
                    throw new InvalidOperationException(
                        $"Matrix column mismatch at row {i}. Left={left[i].Length}, Right={right[i].Length}");
                }
            }
        }
        private static decimal CandleLogRatioDistance(
    decimal[][] current,
    decimal[][] historical,
    decimal weight)
        {
            decimal total = 0m;
            decimal totalWeights = 0m;

            for (int i = 0; i < current.Length; i++)
            {
                decimal rowWeight = (decimal)Math.Log(i + 2);

                for (int j = 0; j < current[i].Length; j++)
                {
                    decimal currentValue = current[i][j];
                    decimal historicalValue = historical[i][j];

                    if (currentValue <= 0m || historicalValue <= 0m)
                        continue;

                    decimal ratio = historicalValue / currentValue;

                    total += rowWeight *
                             (decimal)Math.Abs(Math.Log((double)ratio));

                    totalWeights += rowWeight;
                }
            }

            return totalWeights == 0m
                ? 0m
                : weight * total / totalWeights;
        }
        private static decimal MatrixAbsDistance(
    decimal[][] current,
    decimal[][] historical,
    decimal weight)
        {
            decimal total = 0m;
            decimal totalWeights = 0m;

            for (int i = 0; i < current.Length; i++)
            {
                decimal rowWeight = (decimal)Math.Log(i + 2);

                for (int j = 0; j < current[i].Length; j++)
                {
                    total += rowWeight *
                             Math.Abs(current[i][j] - historical[i][j]);

                    totalWeights += rowWeight;
                }
            }

            return totalWeights == 0m
                ? 0m
                : weight * total / totalWeights;
        }
        decimal[] CrossCorrelation(
    decimal[][] current,
    decimal[][] historical)
        {
            int featureCount = current[0].Length;

            decimal[] correlation = new decimal[featureCount];

            for (int col = 0; col < featureCount; col++)
            {
                correlation[col] =
                    Statistics.CrossCorrelation(
                        current,
                        historical,
                        col);
            }

            return correlation;
        }
        public (decimal distance, decimal[] futureMove, decimal[] stats) CalculateDistance(
    CosmosSlice currentSlice,
    CosmosSlice slice)
        {
            decimal candleDistance =
                CandleLogRatioDistance(currentSlice.candles, slice.candles, candleWeight);

            decimal deltaDistance =
                MatrixAbsDistance(currentSlice.deltas, slice.deltas, deltaWeight);

            decimal overAverageDistance =
                MatrixAbsDistance(currentSlice.overAverages, slice.overAverages, overAverageWeight);

            decimal maDelta5Distance =
                MatrixAbsDistance(currentSlice.MADelta5, slice.MADelta5, MADelta5Weight);

            decimal maDelta10Distance =
                MatrixAbsDistance(currentSlice.MADelta10, slice.MADelta10, MADelta10Weight);

            decimal maOverAverage5Distance =
                MatrixAbsDistance(currentSlice.MAOverAverage5, slice.MAOverAverage5, MAOverAverage5Weight);

            decimal maOverAverage10Distance =
                MatrixAbsDistance(currentSlice.MAOverAverage10, slice.MAOverAverage10, MAOverAverage10Weight);

            //decimal correlationDistance =
            //    MatrixAbsDistance(currentSlice.correlation, slice.correlation, CorrelationWeight);
            decimal[] crossCorrelation = CrossCorrelation(currentSlice.candles, slice.candles);

            decimal correlationDistance = 0m;

            for (int i = 0; i < crossCorrelation.Length; i++)
            {

                decimal corr = Math.Max(
                    -1m,
                    Math.Min(1m, crossCorrelation[i]));

                correlationDistance += 1m - corr;

            }

            correlationDistance /= crossCorrelation.Length;
            correlationDistance *= CorrelationWeight;
            maDelta5Distance = Math.Min(maDelta5Distance, 5m);
            maDelta10Distance = Math.Min(maDelta10Distance, 5m);
            deltaDistance = Math.Min(deltaDistance, 5m);
            decimal totalDistance =
                  candleDistance
                + deltaDistance
            + overAverageDistance
                + maDelta5Distance
                + maDelta10Distance
                + maOverAverage5Distance
                + maOverAverage10Distance
                + correlationDistance;
            var stats = new decimal[8] { candleDistance, deltaDistance, overAverageDistance, maDelta5Distance, maDelta10Distance, maOverAverage5Distance, maOverAverage10Distance, correlationDistance };
    //        Console.WriteLine(
    //$"candle={candleDistance:F6}, " + $"overAverageDistance={overAverageDistance}" + $"maDelta5Distance={maDelta5Distance}" + $"maDelta10Distance={maDelta10Distance}" + $"maOverAverage5Distance={maOverAverage5Distance}" +
    //$"maOverAverage10Distance={maOverAverage10Distance}" + 
    //$"delta={deltaDistance:F6}, " +
    //$"overAvg={overAverageDistance:F6}, " +
    //$"corr={correlationDistance:F6}");

            return (totalDistance, slice.FutureCloseMoves, stats);
        }
        public ForwardProjection CompileWeightedAverage(
            List<Neighbor> winners)
        {
            if (winners.Count == 0)
                return new ForwardProjection();

            winners = winners.Where(win => win.FutureMove != null).ToList();

            int columns = winners[0].FutureMove.Length;

            decimal[] mean = new decimal[columns];
            decimal[] variance = new decimal[columns];
            decimal[] stats = new decimal[winners[0].Stats.Length];
            // ----------------------------
            // weighted mean
            // ----------------------------

            decimal totalWeight = 0m;

            foreach (var winner in winners)
            {
                decimal weight = 1m / (winner.Distance * winner.Distance + 0.000001m);

                totalWeight += weight;

                for (int i = 0; i < columns; i++)
                {
                    if (winner.FutureMove != null)
                        mean[i] += winner.FutureMove[i] * weight;

                }
                for (int i = 0; i < winner.Stats.Length; i++)
                {
                    stats[i] += weight * winner.Stats[i];
                }
            }

            for (int i = 0; i < columns; i++)
            {
                mean[i] /= totalWeight;
            }
            for (int i = 0; i < stats.Length; i++)
            {
                stats[i] /= totalWeight;
            }
            // ----------------------------
            // weighted variance
            // ----------------------------

            foreach (var winner in winners)
            {
                decimal weight = 1m / (winner.Distance * winner.Distance + 0.000001m);

                for (int i = 0; i < columns; i++)
                {
                    if (winner.FutureMove != null)
                    {
                        decimal diff = winner.FutureMove[i] - mean[i];

                        variance[i] +=
                            diff * diff * weight;
                    }
                }
            }

            for (int i = 0; i < columns; i++)
            {
                variance[i] /= totalWeight;
            }

            return new ForwardProjection
            {
                Mean = mean,
                Variance = variance,
                Stats = stats
            };
        }
        public decimal[] FutureMove(decimal[][] deltas)
        {
            if (deltas == null || deltas.Length == 0)
                throw new ArgumentException("Deltas matrix is empty.", nameof(deltas));

            if (deltas.Length <= WindowSize)
            {
                throw new InvalidOperationException(
                    $"Cannot calculate future move. Deltas has {deltas.Length} rows, but WindowSize is {WindowSize}. " +
                    $"The historical slice must include rows after the comparison window.");
            }

            decimal[][] futureRows = deltas
                .Skip(WindowSize)
                .ToArray();

            decimal[] futureMoves = new decimal[InstrumentCount];

            for (int instrument = 0; instrument < InstrumentCount; instrument++)
            {
                int closeColumn = instrument * FeaturesPerInstrument + 3;

                decimal sum = 0m;

                for (int row = 0; row < futureRows.Length; row++)
                {
                    sum += futureRows[row][closeColumn];
                }

                futureMoves[instrument] = sum / futureRows.Length;
            }

            return futureMoves;
        }

    }
    public class ForwardProjection
    {
        public decimal[] Mean { get; set; } = [];
        public decimal[] Variance { get; set; } = [];
        public decimal[] Stats { get; set; } = [];
    }
    public class Neighbor
    {
        public decimal Distance { get; set; }

        public string Id { get; set; } = "";

        public decimal[] FutureMove { get; set; }
        public decimal[] Stats { get; set; } = [];
    }
}
