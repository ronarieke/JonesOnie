module StatisticsComputationModule
open System
open System.IO
open ReferenceModule

let average_delta(lst:List<DateTime*float>) = 
    if lst.Length < 2 then 
        0.
    else
        ([for x in [0..lst.Length - 2] do yield ((fst lst.[x+1]) - (fst lst.[x])).TotalMinutes * (((snd lst.[x+1]) - (snd lst.[x])) / (snd lst.[x]))] |> List.sum) / ((fst lst.[lst.Length - 1]) - (fst lst.[0])).TotalMinutes
let deviation_delta(lst:List<DateTime*float>,average:float) = 
    if lst.Length < 2 then 
        0.
    else
        (([for x in [0..lst.Length - 2] do yield ((((fst lst.[x+1] - (fst lst.[x])).TotalMinutes * (((snd lst.[x+1]) - (snd lst.[x])) / (snd lst.[x]))) - average) ** 2.)] |> List.sum) / ((fst lst.[lst.Length - 1]) - (fst lst.[1])).TotalMinutes ) ** (1./2.)
let correlation_delta(lst:List<DateTime*float>,lst2:List<DateTime*float>,avgS:string,avg2S:string,devS:string,dev2S:string) = 
    let avg = float(avgS)
    let avg2 = float(avg2S)
    let dev = float(devS)
    let dev2 = float(dev2S)

    let ValueFor(date,lst)= 
        snd (lst |> List.find(fun (dte,gam) -> dte = date))
    if lst.Length < 2 || lst2.Length < 2 then 
        0.
    elif ([avg;avg2;dev;dev2] |> List.exists(fun chi -> chi = 0.)) = false then
        if (lst |> List.exists(fun (dte,gam) -> (lst2 |> List.collect(fun (dte2,rho) -> [dte2]) |> List.contains(dte)))) then 
            let dtes = (lst |> List.where(fun (dte, gam) -> (lst2 |> List.collect(fun (dte2,rho) -> [dte2]) |> List.contains(dte))) |> List.collect(fun (dte,gam) -> [dte]))
            if dtes.Length > 2 then 
                ([for x in [0..dtes.Length - 2] do yield (dtes.[x + 1] - dtes.[x]).TotalMinutes * ((ValueFor(dtes.[x+1],lst) - ValueFor(dtes.[x],lst)) / ValueFor(dtes.[x],lst) - avg) * ((ValueFor(dtes.[x+1],lst2) - ValueFor(dtes.[x],lst2)) / ValueFor(dtes.[x],lst2) - avg2)] |> List.sum) / ((dtes.[dtes.Length - 1] - dtes.[1]).TotalMinutes * dev * dev2)
            else
                0.
        else
            0.
     else
        0.
let InfoInstrumentSlice(slice:List<string[]>,instrument_index) = 
    (slice |> List.collect(fun slc -> [(Convert.ToDateTime(slc.[0]),float(slc.[instrument_index]))]))
type AveragesComputation(date:DateTime)=
    let dir = new DirectoryInfo(minute_data)
    let files = [for fle in dir.GetFiles() do yield fle]
    let file = (files |> List.find(fun flex -> flex.Name = FileName(date)))
    let whole_file = ReadAndParseFile(file)
    let exchanges = whole_file.[0]
    let mutable ctr = 1   
    do
        while ContinueIteration(ctr,whole_file.Length) do
            let averages = [for x in [1..exchanges.Length-1] do yield [for infette in whole_file.[ctr..System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)] do yield (Convert.ToDateTime(infette.[0]),float(infette.[x]))] |> List.where(fun (dt,inf) -> inf <> 0.) |> average_delta]
            let avg_fles = [for fle in (new DirectoryInfo(averages_data + @"\Backward")).GetFiles() do yield fle]
            if (avg_fles |> List.exists(fun fle -> fle.Name = FileName(date))) then 
                File.AppendAllText(averages_data + String.Format(@"\Backward\{0}",FileName(date)),"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",averages))
            else
                File.WriteAllText(averages_data + String.Format(@"\Backward\{0}",FileName(date)),String.Join(",",exchanges))
                File.AppendAllText(averages_data + String.Format(@"\Backward\{0}",FileName(date)),"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",averages))
            let forward_averages = [for x in [1..exchanges.Length-1] do yield [for infette in whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)..System.Math.Min(ctr + BackDataLength + ForwardDataLength, whole_file.Length - 1)] do yield (Convert.ToDateTime(infette.[0]),float(infette.[x]))] |> List.where(fun (dt,inf) -> inf <> 0.) |> average_delta]
            let forward_avg_fles = [for fle in (new DirectoryInfo(averages_data + @"\Forward")).GetFiles() do yield fle]
            if (forward_avg_fles |> List.exists(fun fle -> fle.Name = date.Year.ToString() + "_" + date.Month.ToString() + ".csv")) then 
                File.AppendAllText(averages_data + String.Format(@"\Forward\{0}",FileName(date)),"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",forward_averages))
            else
                File.WriteAllText(averages_data + String.Format(@"\Forward\{0}",FileName(date)),String.Join(",",exchanges))
                File.AppendAllText(averages_data + String.Format(@"\Forward\{0}",FileName(date)),"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",forward_averages))
            ctr <- DataIncrement + ctr
type DeviationsComputation(date:DateTime) = 
    let dir = new DirectoryInfo(minute_data)
    let files = [for fle in dir.GetFiles() do yield fle]
    let averages = [for fle in (new DirectoryInfo(averages_data + @"\Backward")).GetFiles() do yield fle] |> List.find(fun fle -> fle.Name = FileName(date))
    let forward_averages = [for fle in (new DirectoryInfo(averages_data + @"\Forward")).GetFiles() do yield fle] |> List.find(fun fle -> fle.Name = FileName(date))
    let file = (files |> List.find(fun flex -> flex.Name = FileName(date)))
    let whole_file = ReadAndParseFile(file)
    let exchanges = whole_file.[0]
    let averages_file = ReadAndParseFile(averages)
    let forward_averages_file = ReadAndParseFile(forward_averages)
    let mutable ctr = 1
    do
        while ContinueIteration(ctr,whole_file.Length) do
            let averages_row = [for st in (averages_file |> List.find(fun row -> row.[0] = whole_file.[ctr].[0])) do yield st]
            let deviations = [for x in [1..exchanges.Length-1] do yield [for infette in whole_file.[ctr..System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)] do yield (Convert.ToDateTime(infette.[0]),float(infette.[x]))] |> List.where(fun (dt,inf) -> inf <> 0.) |> fun (lst) -> deviation_delta(lst, float(averages_row.[x]))]
            let dev_fles = [for fle in (new DirectoryInfo(deviations_data + @"\Backward")).GetFiles() do yield fle]
            if (dev_fles |> List.exists(fun fle -> fle.Name = date.Year.ToString() + "_" + date.Month.ToString() + ".csv")) then 
                File.AppendAllText(deviations_data + String.Format(@"\Backward\{0}",FileName(date)),"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",deviations))
            else
                File.WriteAllText(deviations_data + String.Format(@"\Backward\{0}",FileName(date)),String.Join(",",exchanges))
                File.AppendAllText(deviations_data + String.Format(@"\Backward\{0}",FileName(date)),"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",deviations))
            let fw_averages_row = [for st in (forward_averages_file |> List.find(fun row -> row.[0] = whole_file.[ctr].[0])) do yield st]
            let forward_deviations = [for x in [1..exchanges.Length-1] do yield [for infette in whole_file.[ctr..System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)] do yield (Convert.ToDateTime(infette.[0]),float(infette.[x]))] |> List.where(fun (dt,inf) -> inf <> 0.) |> fun (lst) -> deviation_delta(lst, float(fw_averages_row.[x]))]
            let forward_dev_fles = [for fle in (new DirectoryInfo(deviations_data + @"\Forward")).GetFiles() do yield fle]
            if (forward_dev_fles |> List.exists(fun fle -> fle.Name = FileName(date))) then 
                File.AppendAllText(deviations_data + String.Format(@"\Forward\{0}",FileName(date)),"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",forward_deviations))
            else
                File.WriteAllText(deviations_data + String.Format(@"\Forward\{0}",FileName(date)),String.Join(",",exchanges))
                File.AppendAllText(deviations_data + String.Format(@"\Forward\{0}",FileName(date)),"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",forward_deviations))
            ctr <- DataIncrement + ctr
type CorrelationsComputation(date:DateTime) = 
    let file = [for fle in (new DirectoryInfo(minute_data)).GetFiles() do yield fle] |> List.find(fun fle -> fle.Name = FileName(date))
    let average_file = [for fle in (new DirectoryInfo(averages_data + @"\Backward")).GetFiles() do yield fle] |> List.find(fun fle -> fle.Name = FileName(date))
    let forward_average_file = [for fle in (new DirectoryInfo(averages_data + @"\Forward")).GetFiles() do yield fle] |> List.find(fun fle -> fle.Name = FileName(date))
    let deviation_file = [for fle in (new DirectoryInfo(averages_data + @"\Backward")).GetFiles() do yield fle] |> List.find(fun fle -> fle.Name = FileName(date))
    let forward_deviation_file = [for fle in (new DirectoryInfo(averages_data + @"\Forward")).GetFiles() do yield fle] |> List.find(fun fle -> fle.Name = FileName(date))
    let whole_file = ReadAndParseFile(file)
    let exchanges = whole_file.[0]
    let averages = ReadAndParseFile(average_file)
    let forward_averages = ReadAndParseFile(forward_average_file)
    let deviations = ReadAndParseFile(deviation_file)
    let forward_deviations = ReadAndParseFile(forward_deviation_file)
    let mutable ctr = 1
    let Elongate(lstlst:List<string*List<string*float>>) = 
        String.Join(",",[|for (str,lst) in lstlst do yield String.Join(",",[|for (stri,value) in lst do yield value.ToString()|])|])
    let Square(exchs:string[]) = 
        String.Join(",",[for exch in exchs do yield String.Join(",",[for exch2 in exchs do yield exch + "&" + exch2])])
    do
        while ContinueIteration(ctr,whole_file.Length) do
            let average_row = (averages |> List.find(fun row -> row.[0] = whole_file.[Math.Min(ctr + BackDataLength,whole_file.Length - 1)].[0]))
            let forward_average_row = (forward_averages |> List.find(fun row -> row.[0] = whole_file.[Math.Min(ctr + BackDataLength,whole_file.Length - 1)].[0]))
            let deviation_row = (deviations |> List.find(fun row -> row.[0] = whole_file.[Math.Min(ctr + BackDataLength,whole_file.Length - 1)].[0]))
            let forward_deviation_row = (forward_deviations |> List.find(fun row -> row.[0] = whole_file.[Math.Min(ctr + BackDataLength,whole_file.Length - 1)].[0]))

            let correlations = [for x in [1..exchanges.Length - 1] do yield (exchanges.[x], (whole_file.[ctr..Math.Min(ctr+BackDataLength,whole_file.Length - 1)] |> fun (data) -> [for y in [1..exchanges.Length - 1] do yield (exchanges.[y], correlation_delta(InfoInstrumentSlice(data,x),InfoInstrumentSlice(data,y),average_row.[x],average_row.[y],deviation_row.[x],deviation_row.[y]))]))]
            let corr_fles = [for fle in (new DirectoryInfo(correlations_data+ @"\Backward")).GetFiles() do yield fle ]
            if (corr_fles |> List.exists(fun fle -> fle.Name = FileName(date))) then 
                File.AppendAllText(correlations_data + String.Format(@"\Backward\{0}",FileName(date)),"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+"," + Elongate(correlations))
            else
                File.WriteAllText(correlations_data + String.Format(@"\Backward\{0}",FileName(date)),String.Join(",",Square(exchanges)))
                File.AppendAllText(correlations_data + String.Format(@"\Backward\{0}",FileName(date)),"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+"," + Elongate(correlations))
            let correlations = [for x in [1..exchanges.Length - 1] do yield (exchanges.[x], (whole_file.[ctr+BackDataLength..Math.Min(ctr+BackDataLength+ForwardDataLength,whole_file.Length - 1)] |> fun (data) -> [for y in [1..exchanges.Length - 1] do yield (exchanges.[y], correlation_delta(InfoInstrumentSlice(data,x),InfoInstrumentSlice(data,y),average_row.[x],average_row.[y],deviation_row.[x],deviation_row.[y]))]))]
            let forward_corr_fles = [for fle in (new DirectoryInfo(correlations_data+ @"\Forward")).GetFiles() do yield fle ]
            if (forward_corr_fles |> List.exists(fun fle -> fle.Name = FileName(date))) then 
                File.AppendAllText(correlations_data + String.Format(@"\Forward\{0}",FileName(date)),"\n" + whole_file.[System.Math.Min(ctr + BackDataLength + ForwardDataLength, whole_file.Length - 1)].[0]+"," + Elongate(correlations))
            else
                File.WriteAllText(correlations_data + String.Format(@"\Forward\{0}",FileName(date)),String.Join(",",Square(exchanges)))
                File.AppendAllText(correlations_data + String.Format(@"\Forward\{0}",FileName(date)),"\n" + whole_file.[System.Math.Min(ctr + BackDataLength + ForwardDataLength, whole_file.Length - 1)].[0]+"," + Elongate(correlations))
            ctr <- ctr + DataIncrement
