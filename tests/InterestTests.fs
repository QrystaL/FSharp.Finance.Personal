namespace FSharp.Finance.Personal.Tests

open Xunit
open FsUnit.Xunit

open FSharp.Finance.Personal

module InterestTests =

    let folder = "Interest"

    open Amortisation
    open Calculation
    open DateDay
    open Formatting
    open Interest
    open Scheduling
    open System

    let interestCapExample : Cap = {
        TotalAmount = ValueSome (Amount.Percentage (Percent 100m, Restriction.NoLimit))
        DailyAmount = ValueSome (Amount.Percentage (Percent 0.8m, Restriction.NoLimit))
    }

    module RateTests =

        [<Fact>]
        let ``Zero rate converted to annual yields 0%`` () =
            let actual = Rate.Zero |> Rate.annual
            let expected = Percent 0m
            actual |> should equal expected

        [<Fact>]
        let ``Zero rate converted to daily yields 0%`` () =
            let actual = Rate.Zero |> Rate.daily
            let expected = Percent 0m
            actual |> should equal expected

        [<Fact>]
        let ``36,5% annual converted to daily yields 0,1%`` () =
            let actual = Percent 36.5m |> Rate.Annual |> Rate.daily
            let expected = Percent 0.1m
            actual |> should equal expected

        [<Fact>]
        let ``10% daily converted to daily yields the same`` () =
            let actual = Percent 10m |> Rate.Daily |> Rate.daily
            let expected = Percent 10m
            actual |> should equal expected

        [<Fact>]
        let ``10% annual converted to annual yields the same`` () =
            let actual = Percent 10m |> Rate.Annual |> Rate.annual
            let expected = Percent 10m
            actual |> should equal expected

        [<Fact>]
        let ``0,1% daily converted to annual yields 36,5%`` () =
            let actual = Percent 0.1m |> Rate.Daily |> Rate.annual
            let expected = Percent 36.5m
            actual |> should equal expected

    module CapTests =

        [<Fact>]
        let ``Trying to add €150 interest to a €75 cumulative interest total with no cap on a principal of €200 allows the full €150`` () =
            let actual = Cap.cappedAddedValue Cap.Zero.TotalAmount 200_00L<Cent> 75_00m<Cent> 150_00m<Cent>
            let expected = 150_00m<Cent>
            actual |> should equal expected

        [<Fact>]
        let ``Trying to add €150 interest to a €75 cumulative interest total with a 100% total cap on a principal of €200 only allows €125`` () =
            let actual = Cap.cappedAddedValue interestCapExample.TotalAmount 200_00L<Cent> 75_00m<Cent> 150_00m<Cent>
            let expected = 125_00m<Cent>
            actual |> should equal expected

        [<Fact>]
        let InterestCapTest000 () =
            let title = "InterestCapTest000"
            let description = "Total interest in amortised schedule does not exceed interest cap"
            let sp = {
                AsOfDate = Date(2024, 4, 25)
                StartDate = Date(2023, 2, 9)
                Principal = 499_00L<Cent>
                ScheduleConfig = AutoGenerateSchedule {
                    UnitPeriodConfig = UnitPeriod.Monthly(1, 2023, 2, 14)
                    PaymentCount = 4
                    MaxDuration = Duration.Unlimited
                }
                PaymentConfig = {
                    LevelPaymentOption = LowerFinalPayment
                    ScheduledPaymentOption = AsScheduled
                    CloseBalanceOption = LeaveOpenBalance
                    PaymentRounding = RoundUp
                    MinimumPayment = DeferOrWriteOff 50L<Cent>
                    PaymentTimeout = 3<DurationDay>
                }
                FeeConfig = None
                ChargeConfig = None
                InterestConfig = {
                    Method = Method.Simple
                    StandardRate = Rate.Daily (Percent 0.8m)
                    Cap = interestCapExample
                    InitialGracePeriod = 3<DurationDay>
                    PromotionalRates = [||]
                    RateOnNegativeBalance = Rate.Zero
                    AprMethod = Apr.CalculationMethod.UnitedKingdom 3
                    Rounding = RoundDown
                }
            }

            let actualPayments = Map.empty

            let schedules =
                actualPayments
                |> Amortisation.generate sp (ValueSome SettlementDay.SettlementOnAsOfDay) false

            Schedule.outputHtmlToFile folder title description sp schedules

            let interestPortion = schedules.AmortisationSchedule.ScheduleItems |> Map.maxKeyValue |> snd |> _.InterestPortion

            interestPortion |> should be (lessThanOrEqualTo 499_00L<Cent>)

        [<Fact>]
        let InterestCapTest001 () =
            let title = "InterestCapTest001"
            let description = "Total interest in amortised schedule does not exceed interest cap, using unrounded percentages"
            let sp = {
                AsOfDate = Date(2024, 4, 25)
                StartDate = Date(2023, 2, 9)
                Principal = 499_00L<Cent>
                ScheduleConfig = AutoGenerateSchedule {
                    UnitPeriodConfig = UnitPeriod.Monthly(1, 2023, 2, 14)
                    PaymentCount = 4
                    MaxDuration = Duration.Unlimited
                }
                PaymentConfig = {
                    LevelPaymentOption = LowerFinalPayment
                    ScheduledPaymentOption = AsScheduled
                    CloseBalanceOption = LeaveOpenBalance
                    PaymentRounding = RoundUp
                    MinimumPayment = DeferOrWriteOff 50L<Cent>
                    PaymentTimeout = 3<DurationDay>
                }
                FeeConfig = None
                ChargeConfig = None
                InterestConfig = {
                    Method = Method.Simple
                    StandardRate = Rate.Daily (Percent 0.876m)
                    Cap = { interestCapExample with TotalAmount = ValueSome (Amount.Percentage (Percent 123.45m, Restriction.NoLimit)) }
                    InitialGracePeriod = 3<DurationDay>
                    PromotionalRates = [||]
                    RateOnNegativeBalance = Rate.Zero
                    AprMethod = Apr.CalculationMethod.UnitedKingdom 3
                    Rounding = RoundDown
                }
            }

            let actualPayments = Map.empty

            let schedules =
                actualPayments
                |> Amortisation.generate sp (ValueSome SettlementDay.SettlementOnAsOfDay) false

            Schedule.outputHtmlToFile folder title description sp schedules

            let interestPortion = schedules.AmortisationSchedule.ScheduleItems |> Map.maxKeyValue |> snd |> _.InterestPortion

            interestPortion |> should be (lessThanOrEqualTo 616_01L<Cent>)

    module DailyRatesTests =

        [<Fact>]
        let ``Daily rates with no settlement inside the grace period or promotional rates`` () =
            let startDate = Date(2024, 4, 10)
            let standardRate = Rate.Annual <| Percent 10m
            let promotionalRates = [||]
            let fromDay = 0<OffsetDay>
            let toDay = 10<OffsetDay>
            let actual = dailyRates startDate false standardRate promotionalRates fromDay toDay
            let expected = [| 1 .. 10 |] |> Array.map(fun d -> { RateDay = d * 1<OffsetDay>; InterestRate = Rate.Annual (Percent 10m) })
            actual |> should equal expected

        [<Fact>]
        let ``Daily rates with a settlement inside the grace period, but no promotional rates`` () =
            let startDate = Date(2024, 4, 10)
            let standardRate = Rate.Annual <| Percent 10m
            let promotionalRates = [||]
            let fromDay = 0<OffsetDay>
            let toDay = 10<OffsetDay>
            let actual = dailyRates startDate true standardRate promotionalRates fromDay toDay
            let expected = [| 1 .. 10 |] |> Array.map(fun d -> { RateDay = d * 1<OffsetDay>; InterestRate = Rate.Zero })
            actual |> should equal expected

        [<Fact>]
        let ``Daily rates with no settlement inside the grace period but with promotional rates`` () =
            let startDate = Date(2024, 4, 10)
            let standardRate = Rate.Annual <| Percent 10m
            let promotionalRates = [|
                ({ DateRange = { Start = Date(2024, 4, 10); End = Date(2024, 4, 15) }; Rate = Rate.Annual (Percent 2m) } : PromotionalRate)
            |]
            let fromDay = 0<OffsetDay>
            let toDay = 10<OffsetDay>
            let actual = dailyRates startDate false standardRate promotionalRates fromDay toDay
            let expected =
                [|
                    [| 1 .. 5 |] |> Array.map(fun d -> { RateDay = d * 1<OffsetDay>; InterestRate = Rate.Annual (Percent 2m) })
                    [| 6 .. 10 |] |> Array.map(fun d -> { RateDay = d * 1<OffsetDay>; InterestRate = Rate.Annual (Percent 10m) })
                |]
                |> Array.concat
            actual |> should equal expected

    module Cca2004Tests =

        [<Fact>]
        let Cca2004Test000 () =
            let title = "Cca2004Test000"
            let description = "UK rebate example 1"
            let principal = 5000_00L<Cent>
            let payments = [| 1 .. 48 |] |> Array.map(fun i -> i, 134_57L<Cent>)
            let apr = Percent 14m
            let settlementPeriod = 12
            let settlementPartPeriod = Fraction.Zero
            let unitPeriod = UnitPeriod.Month 1
            let paymentRounding = RoundWith MidpointRounding.AwayFromZero
            let actual = calculateRebate principal payments apr settlementPeriod settlementPartPeriod unitPeriod paymentRounding
            let expected = 860_52L<Cent>
            actual |> should equal expected

        [<Fact>]
        let Cca2004Test001 () =
            let title = "Cca2004Test001"
            let description = "UK rebate example 1a"
            let principal = 5000_00L<Cent>
            let payments = [| 1 .. 48 |] |> Array.map(fun i -> i, 134_57L<Cent>)
            let apr = Percent 14m
            let settlementPeriod = 12
            let settlementPartPeriod = Fraction.Simple (28, 30)
            let unitPeriod = UnitPeriod.Month 1
            let paymentRounding = RoundWith MidpointRounding.AwayFromZero
            let actual = calculateRebate principal payments apr settlementPeriod settlementPartPeriod unitPeriod paymentRounding
            let expected = 819_71L<Cent>
            actual |> should equal expected

        [<Fact>]
        let Cca2004Test002 () =
            let title = "Cca2004Test002"
            let description = "UK rebate example 1b"
            let principal = 5000_00L<Cent>
            let payments = [| 1 .. 48 |] |> Array.map(fun i -> i, 134_57L<Cent>)
            let apr = Percent 14m
            let settlementPeriod = 12
            let settlementPartPeriod = Fraction.Simple (28, 31)
            let unitPeriod = UnitPeriod.Month 1
            let paymentRounding = RoundWith MidpointRounding.AwayFromZero
            let actual = calculateRebate principal payments apr settlementPeriod settlementPartPeriod unitPeriod paymentRounding
            let expected = 821_03L<Cent>
            actual |> should equal expected

        [<Fact>]
        let ``Cca2004Test003`` () =
            let title = "Cca2004Test003"
            let description = "UK rebate example 1c"
            let principal = 5000_00L<Cent>
            let payments = [| 1 .. 48 |] |> Array.map(fun i -> i, 134_57L<Cent>)
            let apr = Percent 14m
            let settlementPeriod = 13
            let settlementPartPeriod = Fraction.Simple (28, 30)
            let unitPeriod = UnitPeriod.Month 1
            let paymentRounding = RoundWith MidpointRounding.AwayFromZero
            let actual = calculateRebate principal payments apr settlementPeriod settlementPartPeriod unitPeriod paymentRounding
            let expected = 776_90L<Cent>
            actual |> should equal expected

        [<Fact>]
        let Cca2004Test004 () =
            let title = "Cca2004Test004"
            let description = "UK rebate example 2"
            let principal = 10000_00L<Cent>
            let payments = [| 1 .. 180 |] |> Array.map(fun i -> i, 139_51L<Cent>)
            let apr = Percent 16m
            let settlementPeriod = 73
            let settlementPartPeriod = Fraction.Zero
            let unitPeriod = UnitPeriod.Month 1
            let paymentRounding = RoundWith MidpointRounding.AwayFromZero
            let actual = calculateRebate principal payments apr settlementPeriod settlementPartPeriod unitPeriod paymentRounding
            let expected = 6702_45L<Cent>
            actual |> should equal expected

        [<Fact>]
        let Cca2004Test005 () =
            let title = "Cca2004Test005"
            let description = "UK rebate example 2a"
            let principal = 10000_00L<Cent>
            let payments = [| 1 .. 180 |] |> Array.map(fun i -> i, 139_51L<Cent>)
            let apr = Percent 16m
            let settlementPeriod = 73
            let settlementPartPeriod = Fraction.Simple (28, 30)
            let unitPeriod = UnitPeriod.Month 1
            let paymentRounding = RoundWith MidpointRounding.AwayFromZero
            let actual = calculateRebate principal payments apr settlementPeriod settlementPartPeriod unitPeriod paymentRounding
            let expected = 6606_95L<Cent>
            actual |> should equal expected

        let scheduleParameters =
            {
                StartDate = Date(2010, 3, 1)
                AsOfDate = Date(2011, 3, 1)
                Principal = 5000_00L<Cent>
                ScheduleConfig = FixedSchedules [| { UnitPeriodConfig = UnitPeriod.Monthly(1, 2010, 4, 1); PaymentCount = 48; PaymentValue = 134_57L<Cent>; ScheduleType = ScheduleType.Original } |]
                PaymentConfig = {
                    LevelPaymentOption = LowerFinalPayment
                    ScheduledPaymentOption = AsScheduled
                    CloseBalanceOption = LeaveOpenBalance
                    PaymentRounding = RoundUp
                    MinimumPayment = DeferOrWriteOff 50L<Cent>
                    PaymentTimeout = 3<DurationDay>
                }
                FeeConfig = None
                ChargeConfig = None
                InterestConfig = {
                    Method = Method.Simple
                    StandardRate = Rate.Annual <| Percent 13.1475m
                    Cap = { TotalAmount = ValueSome <| Amount.Percentage (Percent 100m, Restriction.NoLimit); DailyAmount = ValueSome <| Amount.Percentage (Percent 0.8m, Restriction.NoLimit) }
                    InitialGracePeriod = 0<DurationDay>
                    PromotionalRates = [||]
                    RateOnNegativeBalance = Rate.Annual (Percent 8m)
                    AprMethod = Apr.CalculationMethod.UnitedKingdom 3
                    Rounding = RoundDown
                }
            }

        [<Fact>]
        let Cca2004Test006 () =
            let title = "Cca2004Test006"
            let description = "Initial statement (simple interest) matching total interest amount of £1459.36"
            let sp = { scheduleParameters with AsOfDate = Date(2010, 3, 1) }

            let actualPayments = Map.empty

            let schedules =
                actualPayments
                |> Amortisation.generate sp ValueNone true

            Schedule.outputHtmlToFile folder title description sp schedules

            let levelPayment = schedules.AmortisationSchedule.ScheduleItems[1433<OffsetDay>].ScheduledPayment |> ScheduledPayment.total
            let finalPayment = schedules.AmortisationSchedule.ScheduleItems[1461<OffsetDay>].ScheduledPayment |> ScheduledPayment.total
            let interestPortion = schedules.AmortisationSchedule.ScheduleItems |> Map.values |> Seq.sumBy _.InterestPortion
            [ levelPayment; finalPayment; interestPortion ] |> should equal [ 134_57L<Cent>; 134_57L<Cent>; 1459_36L<Cent> ]

        [<Fact>]
        let Cca2004Test007 () =
            let title = "Cca2004Test007"
            let description = "Initial statement (simple interest, autogenerated payment amounts) matching level payment of £134.57"
            let sp = { scheduleParameters with AsOfDate = Date(2010, 3, 1); ScheduleConfig = AutoGenerateSchedule { UnitPeriodConfig = UnitPeriod.Monthly(1, 2010, 4, 1); PaymentCount = 48; MaxDuration = Duration.Unlimited } }

            let actualPayments = Map.empty

            let schedules =
                actualPayments
                |> Amortisation.generate sp ValueNone true

            Schedule.outputHtmlToFile folder title description sp schedules

            let levelPayment = schedules.AmortisationSchedule.ScheduleItems[1433<OffsetDay>].ScheduledPayment |> ScheduledPayment.total
            let finalPayment = schedules.AmortisationSchedule.ScheduleItems[1461<OffsetDay>].ScheduledPayment |> ScheduledPayment.total
            let interestPortion = schedules.AmortisationSchedule.ScheduleItems |> Map.values |> Seq.sumBy _.InterestPortion
            [ levelPayment; finalPayment; interestPortion ] |> should equal [ 134_57L<Cent>; 134_57L<Cent>; 1459_36L<Cent> ]

        [<Fact>]
        let Cca2004Test008 () =
            let title = "Cca2004Test008"
            let description = "CCA 2004 rebate example using library method (simple interest)"
            let sp = scheduleParameters

            let actualPayments =
                Map [
                    31<OffsetDay>, [| ActualPayment.quickConfirmed 134_57L<Cent> |]
                    61<OffsetDay>, [| ActualPayment.quickConfirmed 134_57L<Cent> |]
                    92<OffsetDay>, [| ActualPayment.quickConfirmed 134_57L<Cent> |]
                    122<OffsetDay>, [| ActualPayment.quickConfirmed 134_57L<Cent> |]
                    153<OffsetDay>, [| ActualPayment.quickConfirmed 134_57L<Cent> |]
                    184<OffsetDay>, [| ActualPayment.quickConfirmed 134_57L<Cent> |]
                    214<OffsetDay>, [| ActualPayment.quickConfirmed 134_57L<Cent> |]
                    245<OffsetDay>, [| ActualPayment.quickConfirmed 134_57L<Cent> |]
                    275<OffsetDay>, [| ActualPayment.quickConfirmed 134_57L<Cent> |]
                    306<OffsetDay>, [| ActualPayment.quickConfirmed 134_57L<Cent> |]
                    337<OffsetDay>, [| ActualPayment.quickConfirmed 134_57L<Cent> |]
                    365<OffsetDay>, [| ActualPayment.quickConfirmed 134_57L<Cent> |]
                ]

            let schedules =
                actualPayments
                |> Amortisation.generate sp (ValueSome SettlementDay.SettlementOnAsOfDay) true

            Schedule.outputHtmlToFile folder title description sp schedules

            let interestPortion = schedules.AmortisationSchedule.ScheduleItems |> Map.values |> Seq.sumBy _.InterestPortion

            interestPortion |> should equal 598_08L<Cent>
