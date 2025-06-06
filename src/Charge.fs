namespace FSharp.Finance.Personal

open Calculation
open DateDay
open Formatting

/// a penalty charge
/// > NB: differences between charge and fee:
/// > - a charge is not an up-front amount, it is incurred as a result of a breach of agreed terms
/// > - a charge is not added to the principal balance and does not therefore accrue interest
module Charge =

    /// options on how to handle multiple charges
    [<Struct; StructuredFormatDisplay("{Html}")>]
    type ChargeGrouping =
        /// only one charge of any type may be applied per day - only the first charge of each type per day is applied
        | OneChargeTypePerDay
        /// only one charge of any type may be applied per schedule - only the first charge of each type per scheduled is applied
        | OneChargeTypePerSchedule
        /// all charges are applied
        | AllChargesApplied
        /// HTML formatting to display the charge grouping in a readable format
        member cg.Html =
            match cg with
            | OneChargeTypePerDay -> "one charge per day"
            | OneChargeTypePerSchedule -> "one charge per schedule"
            | AllChargesApplied -> "all charges applied"

    /// the conditions under which charges are applied
    [<Struct; StructuredFormatDisplay("{Html}")>]
    type ChargeConditions = {
        /// the amount of the charge
        Value: int64<Cent>
        /// whether to group charges by type per day
        ChargeGrouping: ChargeGrouping
        /// any period during which no charges are payable
        ChargeHolidays: DateRange array
    } with
        /// HTML formatting to display the charge conditions in a readable format
        member cc.Html =
            $"<td>{formatCent cc.Value}</td>"
            + $"<td>{cc.ChargeGrouping}</td>"
            + $"<td>{Array.toStringOrNa cc.ChargeHolidays}</td>"

    /// the conditions under which charges are applied
    module ChargeConditions =
        /// generates a range of offset days during which charges are not incurred
        let getHolidays startDate chargeHolidays =
            chargeHolidays
            |> Array.collect(fun (dr: DateRange) ->
                [| (dr.Start - startDate).Days .. (dr.End - startDate).Days |]
                |> Array.map ((*) 1<OffsetDay>)
            )

    /// the type and conditions of any charge
    [<Struct; StructuredFormatDisplay("{Html}")>]
    type ChargeType =
        /// a charge incurred because a scheduled payment was not made on time or in full
        | LatePayment
        /// a charge incurred to cover banking costs when a payment attempt is rejected due to a lack of funds
        | InsufficientFunds
        /// any other type of penalty charge
        | CustomCharge of string
        /// HTML formatting to display the charge type in a readable format
        member ct.Html =
            match ct with
            | LatePayment -> $"late payment"
            | InsufficientFunds -> $"insufficient funds"
            | CustomCharge name -> name

    /// options specifying the types of charges, their amounts, and any restrictions on these
    type Config = {
        /// a list of penalty charges that can be incurred
        ChargeTypes: Map<ChargeType, ChargeConditions>
    }

    /// options specifying the types of charges, their amounts, and any restrictions on these
    module Config =
        /// formats the charge config as an HTML table
        let toHtmlTable (config: Config option) =
            if config.IsNone || config.Value.ChargeTypes.IsEmpty then
                "no charges"
            else
                "<table>"
                    + "<tr>"
                        + "<th>Type</th>"
                        + "<th>Charge</th>"
                        + "<th>Grouping</th>"
                        + "<th>Holidays</th>"
                    + "</tr>"
                    + (config.Value.ChargeTypes |> Map.map(fun ct cc -> $"<tr><td>{ct}</td>{cc}</tr>") |> Map.values |> String.concat "")
                + "</table>"
