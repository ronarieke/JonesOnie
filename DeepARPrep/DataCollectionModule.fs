module DataCollectionModule
open ReferenceModule
open System
open System.Text
open System.IO
open SimbaForex2
open SimbaForex2.Models.OandaModel

let not_interesting = ["USD_SGD"; "USD_THD"; "USD_SAR"; "ZAR_JPY"; "EUR_DKK"]
type DataCollection(date:DateTime) =

    let start_dates() = 
        let month = date.Month
        let year = date.Year
        let thirtyOnes = [1;3;5;7;8;10;12]
        let thirties = [4;6;9;11]
        if thirtyOnes |> List.contains(month) then
            [for x in [1..31] do yield DateTime(year,month,x,16,0,0)] |> List.where(fun dte -> dte.DayOfWeek.Equals(DayOfWeek.Sunday))
        elif thirties |> List.contains(month) then  
            [for x in [1..30] do yield DateTime(year,month,x,16,0,0)] |> List.where(fun dte -> dte.DayOfWeek.Equals(DayOfWeek.Sunday))
        else
            if year % 4 = 0 then 
                [for x in [1..29] do yield DateTime(year,month,x,16,0,0)] |> List.where(fun dte -> dte.DayOfWeek.Equals(DayOfWeek.Sunday))
            else
                [for x in [1..28] do yield DateTime(year,month,x,16,0,0)] |> List.where(fun dte -> dte.DayOfWeek.Equals(DayOfWeek.Sunday))
    let normalized(cndle:Candlestick) = 
        (float(cndle.mid.h + cndle.mid.l) + 2. * float(cndle.mid.o) + 2. * float(cndle.mid.c)) / 6.
    do
        let instruments = OandaConn.GetInstruments(access_tuple).instruments
        let exchanges = ([for instr in instruments do yield instr.name] |> List.where(fun ex -> false = (not_interesting |> List.contains(ex))))
        let candles = [for dte in start_dates() do yield (dte, [ for exchange in exchanges do yield (exchange, OandaConn.HistoricalDataWeek(access_tuple,exchange,dte).candles)])]
        let mutable st = "Time"
        for exchange in exchanges do
            st <- st+","+exchange
        File.WriteAllText(minute_data+"/"+FileName(date),st)
        for (dte,candleSet) in candles do
            for x in [0..3599] do
                let mutable ist = "\n" + (snd candleSet.[0]).[x].time
                for exchange in exchanges do
                    try
                        ist <- ist + "," + normalized([for chi in (snd (candleSet |> List.find(fun (exch,cndS) -> exch = exchange))) do yield chi] |> List.find(fun cndle -> cndle.time = (snd candleSet.[0]).[x].time)).ToString()
                    with
                        | e -> 
                            ist <- ist + ",0.000"   
                File.AppendAllText(minute_data+"/"+date.Year.ToString()+"_"+date.Month.ToString()+".csv",ist)