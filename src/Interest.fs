namespace FSharp.Finance.Personal

open System

/// methods for calculating interest and unambiguously expressing interest rates, as well as enforcing regulatory caps on interest chargeable
module Interest =

    open Calculation
    open DateDay

    /// the interest rate expressed as either an annual or a daily rate
    [<RequireQualifiedAccess; Struct; StructuredFormatDisplay("{Html}")>]
    type Rate =
        /// a zero rate
        | Zero
        /// the annual interest rate, or the daily interest rate multiplied by 365
        | Annual of Annual:Percent
        /// the daily interest rate, or the annual interest rate divided by 365
        | Daily of Daily:Percent
        /// HTML formatting to display the rate in a readable format
        member r.Html =
            match r with
            | Zero -> "zero"
            | Annual p -> $"{p} per year"
            | Daily p -> $"{p} per day"

    module Rate =
        /// calculates the annual interest rate from the daily one
        let annual = function
            | Rate.Zero -> Percent 0m
            | Rate.Annual (Percent air) -> air |> Percent
            | Rate.Daily (Percent dir) -> dir * 365m |> Percent

        /// calculates the daily interest rate from the annual one
        let daily = function
            | Rate.Zero -> Percent 0m
            | Rate.Annual (Percent air) -> air / 365m |> Percent
            | Rate.Daily (Percent dir) -> dir |> Percent

    /// the daily interest rate
    [<Struct>]
    type DailyRate = {
        /// the day expressed as an offset from the start date
        RateDay: int<OffsetDay>
        /// the interest rate applicable on the given day
        InterestRate: Rate
    }

    /// caps on the total interest accruable
    [<RequireQualifiedAccess; Struct; StructuredFormatDisplay("{Html}")>]
    type Cap = {
        /// a cap on the total amount of interest chargeable over the entire schedule
        TotalAmount: Amount voption
        /// a cap on the daily amount of interest chargeable
        DailyAmount: Amount voption
    }
    with
        /// HTML formatting to display the cap in a readable format
        member c.Html =
            let total = match c.TotalAmount with ValueSome a -> $"{a}" | ValueNone -> "<i>n/a</i>"
            let daily = match c.DailyAmount with ValueSome a -> $"{a}" | ValueNone -> "<i>n/a</i>"
            $"total {total}; daily {daily}"
    
    /// caps on the total interest accruable
    module Cap =
        /// no cap
        let Zero = {
            Cap.TotalAmount = ValueNone
            Cap.DailyAmount = ValueNone
        }

        /// if the base value plus the added value exceeds the cap total, return the difference between the cap total and the base value
        let cappedAddedValue capAmount principalBalance baseValue addedValue =
            match capAmount with
            | ValueSome amount ->
                let capTotal = Amount.total principalBalance amount |> max 0m<Cent>
                if baseValue + addedValue > capTotal then
                    capTotal - baseValue
                else
                    addedValue
            | ValueNone ->
                addedValue

    /// a promotional interest rate valid during the specified date range
    [<RequireQualifiedAccess; Struct; StructuredFormatDisplay("{Html}")>]
    type PromotionalRate = {
        DateRange: DateRange
        Rate: Rate
    }
    with
        /// HTML formatting to display the cap in a readable format
        member c.Html =
            $"{c.DateRange} at {c.Rate}"
    
    /// a promotional interest rate valid during the specified date range
    module PromotionalRate =
        /// creates a map of offset days and promotional interest rates
        let toMap (startDate: Date) (promotionalRates: PromotionalRate array) =
            promotionalRates
            |> Array.collect(fun pr ->
                [| (pr.DateRange.Start - startDate).Days .. (pr.DateRange.End - startDate).Days |]
                |> Array.map(fun d -> d, pr.Rate)
            )
            |> Map.ofArray

    /// the method used to calculate the interest
    [<RequireQualifiedAccess; Struct; StructuredFormatDisplay("{Html}")>]
    type Method =
        /// simple interest method, where interest is based on the principal balance and the number of days outstanding
        | Simple
        /// add-on interest method, where the interest accrued over the loan is added to the initial balance and the interest is paid off before the principal balance
        | AddOn
        /// HTML formatting to display the method in a readable format
        member m.Html =
            match m with
            | Simple -> "simple"
            | AddOn -> "add-on"

    /// interest options
    [<Struct>]
    type Config = {
        /// the method for calculating interest
        Method: Method
        /// the standard rate of interest
        StandardRate: Rate
        /// any total or daily caps on interest
        Cap: Cap
        /// any grace period at the start of a schedule, during which if settled no interest is payable
        InitialGracePeriod: int<DurationDay>
        /// any promotional or introductory offers during which a different interest rate is applicable
        PromotionalRates: PromotionalRate array
        /// the interest rate applicable for any period in which a refund is owing
        RateOnNegativeBalance: Rate
        /// how to round interest
        Rounding: Rounding
        /// which APR calculation method to use
        AprMethod: Apr.CalculationMethod
    }

    /// interest options
    module Config =
        /// formats the interest config as an HTML table
        let toHtmlTable config =
            "<table>"
                + "<tr>"
                    + $"<td>standard rate: <i>{config.StandardRate}</i></td>"
                    + $"<td>method: <i>{config.Method}</i></td>"
                + "</tr>"
                + "<tr>"
                    + $"<td>rounding: <i>{config.Rounding}</i></td>"
                    + $"<td>APR method: <i>{config.AprMethod}</i></td>"
                + "</tr>"
                + "<tr>"
                    + $"<td>initial grace period: <i>{config.InitialGracePeriod} day(s)</i></td>"
                    + $"<td>rate on negative balance: <i>{config.RateOnNegativeBalance}</i></td>"
                + "</tr>"
                + "<tr>"
                    + $"""<td colspan="2">promotional rates: <i>{Array.toStringOrNa config.PromotionalRates}</i></td>"""
                + "</tr>"
                + "<tr>"
                    + $"""<td colspan="2">cap: <i>{config.Cap}</td>"""
                + "</tr>"
            + "</table>"

    /// calculates the interest chargeable on a range of days
    let dailyRates (startDate: Date) isSettledWithinGracePeriod standardRate promotionalRates (fromDay: int<OffsetDay>) (toDay: int<OffsetDay>) =
        let promoRates = promotionalRates |> PromotionalRate.toMap startDate

        [| int fromDay + 1 .. int toDay |]
        |> Array.map(fun d ->
            let offsetDay = d * 1<OffsetDay>
            if isSettledWithinGracePeriod then
                { RateDay = offsetDay; InterestRate = Rate.Zero }
            else
                match promoRates |> Map.tryFind d with
                | Some rate -> { RateDay = offsetDay; InterestRate = rate }
                | None -> { RateDay = offsetDay; InterestRate = standardRate }
        )

    /// calculates the interest accrued on a balance at a particular interest rate over a number of days, optionally capped by a daily amount
    let calculate (principalBalance: int64<Cent>) (dailyInterestCap: Amount voption) interestRounding (dailyRates: DailyRate array) =
        dailyRates
        |> Array.sumBy (fun dr ->
            let rate = dr.InterestRate |> Rate.daily |> Percent.toDecimal
            let interest = decimal principalBalance * rate * 1m<Cent>
            Cap.cappedAddedValue dailyInterestCap principalBalance 0m<Cent> interest
        )
        |> Cent.roundTo interestRounding 8

    /// calculates the settlement figure based on Consumer Credit (Early Settlement) Regulations 2004 regulation 4(1)
    let internal ``CCA 2004 regulation 4(1) formula`` (A: Map<int, decimal<Cent>>) (B: Map<int, decimal<Cent>>) (r: decimal) (m: int) (n: int) (a: Map<int, int>) (b: Map<int, int>) =
        if A.Count < m || B.Count < n || a.Count < m || b.Count < n then
            0m<Cent>
        else
            let round = Cent.roundTo (RoundWith MidpointRounding.AwayFromZero) 2
            let sum1 =
                [1 .. m]
                |> List.sumBy(fun i -> A[i] * (1m + r |> powi a[i] |> decimal) |> round)
            let sum2 =
                [1 .. n]
                |> List.sumBy(fun j -> B[j] * (1m + r |> powi b[j] |> decimal) |> round)
            sum1 - sum2

    /// calculates the amount of rebate due following an early settlement
    let calculateRebate (principal: int64<Cent>) (payments: (int * int64<Cent>) array) (apr: Percent) (settlementPeriod: int) (settlementPartPeriod: Fraction) unitPeriod paymentRounding =
        if payments |> Array.isEmpty then
            0L<Cent>
        else
            let advanceMap = Map [ (1, decimal principal * 1m<Cent>) ]
            let paymentMap = payments |> Array.map(fun (i, p) -> i, decimal p * 1m<Cent>) |> Map.ofArray
            let aprUnitPeriodRate = apr |> Apr.ukUnitPeriodRate unitPeriod |> Percent.toDecimal |> Rounding.roundTo (RoundWith MidpointRounding.AwayFromZero) 6
            let advanceCount = 1
            let paymentCount = payments |> Array.length |> min settlementPeriod
            let advanceIntervalMap = Map [ (1, settlementPeriod) ]
            let paymentIntervalMap = payments |> Array.take paymentCount |> Array.scan(fun state p -> fst state + 1, int settlementPeriod - int (fst p)) (0, settlementPeriod) |> Array.tail |> Map.ofArray
            let addPartPeriodTotal (sf: decimal<Cent>) = settlementPartPeriod |> fun spp -> sf * (1m + aprUnitPeriodRate |> powm (Fraction.toDecimal spp) |> decimal)
            let wholePeriodTotal = ``CCA 2004 regulation 4(1) formula`` advanceMap paymentMap aprUnitPeriodRate advanceCount paymentCount advanceIntervalMap paymentIntervalMap
            let settlementFigure = wholePeriodTotal |> addPartPeriodTotal |> Cent.fromDecimalCent paymentRounding
            let remainingPaymentTotal = payments |> Array.filter (fun (i, _) -> i > settlementPeriod) |> Array.sumBy snd
            remainingPaymentTotal - settlementFigure

    /// if there is less than one cent remaining, discards any fraction
    let ignoreFractionalCents (multiplier: int) i =
        if abs i < decimal multiplier * 1m<Cent> then 0m<Cent> else i
