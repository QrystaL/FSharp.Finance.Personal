(**
---
title: Payment schedule examples
category: Examples
categoryindex: 2
index: 4
description: Examples of payment schedules
keywords: payment schedule unit-period
---
*)

(**
# Payment Schedule Examples

## Basic example #1

The following example shows the scheduled for a car loan of £10,000 taken out on 7 February 2024 with 36 monthly repayments:

*)

#r "nuget:FSharp.Finance.Personal"

open FSharp.Finance.Personal
open Calculation
open DateDay
open Scheduling

let scheduleParameters =
    {
        AsOfDate = Date(2024, 02, 07)
        StartDate = Date(2024, 02, 07)
        Principal = 10000_00L<Cent>
        ScheduleConfig = AutoGenerateSchedule {
            UnitPeriodConfig = UnitPeriod.Monthly(1, 2024, 3, 7)
            PaymentCount = 36
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
            Method = Interest.Method.Simple
            StandardRate = Interest.Rate.Annual (Percent 6.9m)
            Cap = Interest.Cap.Zero
            InitialGracePeriod = 0<DurationDay>
            PromotionalRates = [||]
            RateOnNegativeBalance = Interest.Rate.Zero
            Rounding = RoundDown
            AprMethod = Apr.CalculationMethod.UnitedKingdom 3
        }
    }
    
let schedule = scheduleParameters |> calculate

schedule

(*** include-it ***)

(**
It is possible to format the `Items` property as an HTML table:
*)

let html = schedule |> SimpleSchedule.toHtmlTable

$"""<div style="overflow-x: auto;">{html}</div>"""

(*** include-it-raw ***)
