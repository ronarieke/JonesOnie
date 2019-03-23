module StatisticsComputationModule
open System
open System.IO
open System.Text
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
type AveragesComputation(date:DateTime)=

    do
        let dir = new DirectoryInfo(minute_data)
        let files = [for fle in dir.GetFiles() do yield fle]
        let file = (files |> List.find(fun flex -> flex.Name = FileName(date)))
        use strm = file.OpenText()
        let mutable whole_file = []
        let mutable exchanges = strm.ReadLine().Split(',')
        let mutable row = strm.ReadLine().Split(',')
        while (Convert.ToDateTime(row.[0]) - date).TotalMinutes < 0. do
            row <- strm.ReadLine().Split(',')
        whole_file <- [row]
        let mutable cancellation = false
        while cancellation <> true do
            whole_file <- row::whole_file
            let str = strm.ReadLine()
            if str <> null then 
                row <- str.Split(',')
            else
                cancellation <- true

        let mutable stillReading = true
        let mutable ctr = 0
        while stillReading do
            let averages = [for x in [1..exchanges.Length-1] do yield [for infette in whole_file.[ctr..System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)] do yield (Convert.ToDateTime(infette.[0]),float(infette.[x]))] |> List.where(fun (dt,inf) -> inf <> 0.) |> average_delta]
            let avg_fles = [for fle in (new DirectoryInfo(averages_data + @"\Backward")).GetFiles() do yield fle]
            if (avg_fles |> List.exists(fun fle -> fle.Name = FileName(date))) then 
                File.AppendAllText(averages_data + String.Format(@"\Backward\{0}",FileName(date)),@"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",averages))
            else
                File.WriteAllText(averages_data + String.Format(@"\Backward\{0}",FileName(date)),String.Join(",",exchanges))
                File.AppendAllText(averages_data + String.Format(@"\Backward\{0}",FileName(date)),@"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",averages))
            let forward_averages = [for x in [1..exchanges.Length-1] do yield [for infette in whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)..System.Math.Min(ctr + BackDataLength + ForwardDataLength, whole_file.Length - 1)] do yield (Convert.ToDateTime(infette.[0]),float(infette.[x]))] |> List.where(fun (dt,inf) -> inf <> 0.) |> average_delta]
            let forward_avg_fles = [for fle in (new DirectoryInfo(averages_data + @"\Forward")).GetFiles() do yield fle]
            if (forward_avg_fles |> List.exists(fun fle -> fle.Name = date.Year.ToString() + "_" + date.Month.ToString() + ".csv")) then 
                File.AppendAllText(averages_data + String.Format(@"\Forward\{0}",FileName(date)),@"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",forward_averages))
            else
                File.WriteAllText(averages_data + String.Format(@"\Forward\{0}",FileName(date)),String.Join(",",exchanges))
                File.AppendAllText(averages_data + String.Format(@"\Forward\{0}",FileName(date)),@"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",forward_averages))
            
            if ctr + BackDataLength + ForwardDataLength > whole_file.Length  then 
                stillReading <- false
            else
                ctr <- DataIncrement + ctr

type DeviationsComputation(date:DateTime) = 

       
    do
        let dir = new DirectoryInfo(minute_data)
        let files = [for fle in dir.GetFiles() do yield fle]
        let averages = [for fle in (new DirectoryInfo(averages_data + @"\Backward")).GetFiles() do yield fle] |> List.find(fun fle -> fle.Name = date.Year.ToString())
        let forward_averages = [for fle in (new DirectoryInfo(averages_data + @"\Forward")).GetFiles() do yield fle] |> List.find(fun fle -> fle.Name = date.Year.ToString())
        
        let file = (files |> List.find(fun flex -> flex.Name = FileName(date)))
        use strm = file.OpenText()
        let mutable whole_file = []
        let mutable exchanges = strm.ReadLine().Split(',')
        let mutable row = strm.ReadLine().Split(',')
        while (Convert.ToDateTime(row.[0]) - date).TotalMinutes < 0. do
            row <- strm.ReadLine().Split(',')
        whole_file <- [row]
        let mutable cancellation = false
        while cancellation <> true do
            whole_file <- row::whole_file
            let str = strm.ReadLine()
            if str <> null then 
                row <- str.Split(',')
            else
                cancellation <- true
        use avgs_strm = averages.OpenText()
        exchanges <- avgs_strm.ReadLine().Split(',')
        row <- avgs_strm.ReadLine().Split(',')
        while (Convert.ToDateTime(row.[0]) - date).TotalMinutes < 0. do
            row <- avgs_strm.ReadLine().Split(',')
        let mutable averages_file = [row]
        cancellation <- false
        while cancellation <> true do
            averages_file <- row::averages_file
            let str = avgs_strm.ReadLine()
            if str <> null then 
                row <- str.Split(',')
            else
                cancellation <- true
        use fw_avgs_strm = forward_averages.OpenText()
        exchanges <- fw_avgs_strm.ReadLine().Split(',')
        row <- fw_avgs_strm.ReadLine().Split(',')
        while (Convert.ToDateTime(row.[0]) - date).TotalMinutes < 0. do
            row <- fw_avgs_strm.ReadLine().Split(',')
        let mutable forward_averages_file = [row]
        cancellation <- false
        while cancellation <> true do
            forward_averages_file <- row::forward_averages_file
            let str = fw_avgs_strm.ReadLine()
            if str <> null then 
                row <- str.Split(',')
            else
                cancellation <- true
        
        let mutable stillReading = true
        let mutable ctr = 0
        while stillReading do
            let averages_row = [for st in (averages_file |> List.find(fun row -> row.[0] = whole_file.[ctr].[0])) do yield st]

            let deviations = [for x in [1..exchanges.Length-1] do yield [for infette in whole_file.[ctr..System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)] do yield (Convert.ToDateTime(infette.[0]),float(infette.[x]))] |> List.where(fun (dt,inf) -> inf <> 0.) |> fun (lst) -> deviation_delta(lst, float(averages_row.[x]))]
            let dev_fles = [for fle in (new DirectoryInfo(deviations_data + @"\Backward")).GetFiles() do yield fle]
            if (dev_fles |> List.exists(fun fle -> fle.Name = date.Year.ToString() + "_" + date.Month.ToString() + ".csv")) then 
                File.AppendAllText(deviations_data + String.Format(@"\Backward\{0}",FileName(date)),@"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",deviations))
            else
                File.WriteAllText(deviations_data + String.Format(@"\Backward\{0}",FileName(date)),String.Join(",",exchanges))
                File.AppendAllText(deviations_data + String.Format(@"\Backward\{0}",FileName(date)),@"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",deviations))
            let fw_averages_row = [for st in (forward_averages_file |> List.find(fun row -> row.[0] = whole_file.[ctr].[0])) do yield st]

            let forward_deviations = [for x in [1..exchanges.Length-1] do yield [for infette in whole_file.[ctr..System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)] do yield (Convert.ToDateTime(infette.[0]),float(infette.[x]))] |> List.where(fun (dt,inf) -> inf <> 0.) |> fun (lst) -> deviation_delta(lst, float(fw_averages_row.[x]))]
            let forward_dev_fles = [for fle in (new DirectoryInfo(deviations_data + @"\Forward")).GetFiles() do yield fle]
            if (forward_dev_fles |> List.exists(fun fle -> fle.Name = FileName(date))) then 
                File.AppendAllText(deviations_data + String.Format(@"\Forward\{0}",FileName(date)),@"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",forward_deviations))
            else
                File.WriteAllText(deviations_data + String.Format(@"\Forward\{0}",FileName(date)),String.Join(",",exchanges))
                File.AppendAllText(deviations_data + String.Format(@"\Forward\{0}",FileName(date)),@"\n" + whole_file.[System.Math.Min(ctr + BackDataLength, whole_file.Length - 1)].[0]+","+String.Join(",",forward_deviations))
            
            if ctr + BackDataLength + ForwardDataLength > whole_file.Length  then 
                stillReading <- false
            else
                ctr <- DataIncrement + ctr
