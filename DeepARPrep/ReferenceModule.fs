module ReferenceModule
open System
open System.IO

let access_token = "<Your OANDA Access Token>"
let account_id = "<Your OANDA Account ID>"
let url = "https://api-fxtrade.oanda.com"
let access_tuple = Tuple.Create(url,account_id,access_token)
let minute_data = @"C:\JonesOnie\CSV\MinuteData"
let statistics_root = @"C:\JonesOnie\CSV\Statistics"
let averages_data = statistics_root + @"\Averages"
let deviations_data = statistics_root + @"\Deviations"
let correlations_data = statistics_root + @"\Correlations"
let DataIncrement = 5
let BackDataLength = 120
let ForwardDataLength = 60


let FileName(date:DateTime) = 
    String.Format("{0}_{1}.csv", date.Year,date.Month)
let ReadAndParseFile(file:FileInfo)= 
    use strm = file.OpenText()
    let header = strm.ReadLine().Split(',')
    let mutable row = strm.ReadLine().Split(',')
    let mutable resultant = [row]
    let mutable cancellation = false
    while cancellation <> true do
        let str = strm.ReadLine()
        if str <> null then 
            resultant<-str.Split(',')::resultant
        else
           cancellation <- true
    resultant <- header::(resultant |> List.sortBy(fun row -> Convert.ToDateTime(row.[0])))
    resultant
let ContinueIteration(ctr,whole_file_length) = 
    ctr + BackDataLength + DataIncrement + ForwardDataLength < whole_file_length 


        