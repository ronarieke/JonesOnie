// Learn more about F# at http://fsharp.org
// See the 'F# Tutorial' project for more help.
open System
open SimbaForex2.Models.OandaModel
open SimbaForex2
open System.Text
open System.IO
open DataCollectionModule
open StatisticsComputationModule

[<EntryPoint>]
let main argv = 
    //for month in [8..12] do
        //let manchu = DataCollection(new DateTime(2005,month,1))
    //    Console.WriteLine("Done for month {0}", month)
    let AvgComp = AveragesComputation(new DateTime(2005,1,1))
    let DevComp = DeviationsComputation(new DateTime(2005,1,1))
    let CorrComp = CorrelationsComputation(new DateTime(2005,1,1))
    Console.WriteLine("Press any key to continue");
    let foo = Console.ReadKey()
    0 // return an integer exit code
