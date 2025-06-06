namespace FSharp.Finance.Personal

open Scheduling

/// functions for handling received payments and calculating interest and/or charges where necessary
module AppliedPayment =

    open Calculation
    open DateDay
    open Formatting

    /// a charge applied to an actual payment
    [<Struct; StructuredFormatDisplay("{Html}")>]
    type AppliedCharge = {
        /// the type of charge
        ChargeType: Charge.ChargeType
        /// the total charge
        Total: int64<Cent>
    }
        with
            /// HTML formatting to display the applied charge in a readable format
            member ac.Html =
                $"<i>{ac.ChargeType}</i> {formatCent ac.Total}"

     /// an actual payment made on a particular day, optionally with charges applied, with the net effect and payment status calculated
    [<Struct>]
    type AppliedPayment = {
        /// the amount of any scheduled payment due on the current day
        ScheduledPayment: ScheduledPayment
        /// the amounts of any actual payments made on the current day
        ActualPayments: ActualPayment array
        /// a payment generated by the system e.g. to calculate a settlement figure
        GeneratedPayment: GeneratedPayment
        /// details of any charges applied
        AppliedCharges: AppliedCharge array
        /// the net effect of any payments made on the current day
        NetEffect: int64<Cent>
        /// the payment status based on the payments made on the current day
        PaymentStatus: PaymentStatus
    }

    /// groups payments by day, applying actual payments, adding a payment status and optionally a late payment charge if underpaid
    let applyPayments asOfDay startDate settlementDay (chargeConfig: Charge.Config option) paymentTimeout (actualPayments: Map<int<OffsetDay>, ActualPayment array>) (scheduledPayments: Map<int<OffsetDay>, ScheduledPayment>) =
        // guard against empty maps
        if Map.isEmpty scheduledPayments then
            Map.empty
        else
            // check to see if any pending payments have timed out
            let actualPayments =
                actualPayments
                |> Map.map(fun d app ->
                    if isTimedOut paymentTimeout asOfDay d then
                        app
                        |> Array.map(fun ap ->
                            match ap.ActualPaymentStatus with
                            | ActualPaymentStatus.Pending p ->
                                { ap with ActualPaymentStatus = ActualPaymentStatus.TimedOut p }
                            | _ ->
                                ap
                        )
                    else
                        app
                )
            // get a list of unique days on which either a scheduled payment is due or an actual payment is made
            let days = [| scheduledPayments |> Map.keys |> Seq.toArray; actualPayments |> Map.keys |> Seq.toArray |] |> Array.concat |> Array.distinct |> Array.sort
            // create a map of charge holidays
            let chargeHolidays =
                match chargeConfig with
                | Some cc ->
                    cc.ChargeTypes
                    |> Map.map(fun ct cc ->
                        Charge.ChargeConditions.getHolidays startDate cc.ChargeHolidays
                    )
                | None ->
                    Map.empty
            // create a map of scheduled payments
            // create a map of applied payments
            let appliedPaymentMap =
                days
                |> Array.mapFold(fun aggregateAppliedCharges offsetDay ->
                    // get any scheduled payment due on the day
                    let scheduledPayment' =
                        scheduledPayments
                        |> Map.tryFind offsetDay
                        |> Option.defaultValue ScheduledPayment.zero
                    // get any actual payments made on the day    
                    let actualPayments' =
                        actualPayments
                        |> Map.tryFind offsetDay
                        |> Option.defaultValue [||]
                    // of the actual payments made on the day, sum any that are confirmed or written off
                    let confirmedPaymentTotal =
                        actualPayments'
                        |> Array.sumBy(fun ap -> match ap.ActualPaymentStatus with ActualPaymentStatus.Confirmed ap -> ap | ActualPaymentStatus.WriteOff ap -> ap | _ -> 0L<Cent>)
                    // of the actual payments made on the day, sum any that are still pending
                    let pendingPaymentTotal =
                        actualPayments'
                        |> Array.sumBy(fun ap -> match ap.ActualPaymentStatus with ActualPaymentStatus.Pending ap -> ap | _ -> 0L<Cent>)
                    // calculate the net effect and payment status for the day
                    let netEffect, paymentStatus =
                        // if a payment is pending, this overrides any other net effect or status for the day
                        if pendingPaymentTotal > 0L<Cent> then
                            pendingPaymentTotal + confirmedPaymentTotal, PaymentPending
                        // otherwise, calculate as normal
                        else

                            match ScheduledPayment.total scheduledPayment', confirmedPaymentTotal with
                            // no payments due or made (possibly the day is included for information only, e.g. to force calculation of balances)
                            | 0L<Cent>, 0L<Cent> ->
                                0L<Cent>, NoneScheduled
                            // no payment due, but a refund issued
                            | 0L<Cent>, cpt when cpt < 0L<Cent> ->
                                cpt, Refunded
                            // no payment due, but a payment made
                            | 0L<Cent>, cpt ->
                                cpt, ExtraPayment
                            // a payment due on or before the day
                            | spt, cpt when cpt < spt && offsetDay <= asOfDay && int offsetDay + int paymentTimeout >= int asOfDay ->
                                match settlementDay with
                                // settlement requested on a future day
                                | ValueSome (SettlementDay.SettlementOn day) when day > offsetDay ->
                                    0L<Cent>, PaymentDue
                                | ValueSome SettlementDay.SettlementOnAsOfDay when asOfDay > offsetDay ->
                                    0L<Cent>, PaymentDue
                                // settlement requested on the day, requiring a generated payment to be calculated (calculation deferred until amortisation schedule is generated)
                                | ValueSome (SettlementDay.SettlementOn day) when day = offsetDay ->
                                    0L<Cent>, Generated
                                | ValueSome SettlementDay.SettlementOnAsOfDay when asOfDay = offsetDay ->
                                    0L<Cent>, Generated
                                // no settlement on day, or statement requested
                                | _ ->
                                    spt, PaymentDue
                            // a payment due on a future day
                            | spt, _ when offsetDay > asOfDay ->
                                spt, NotYetDue
                            // a payment due but no payment made
                            | spt, 0L<Cent> when spt > 0L<Cent> ->
                                0L<Cent>, MissedPayment
                            // a payment due but the payment made is less than what's due
                            | spt, cpt when cpt < spt ->
                                cpt, Underpayment
                            // a payment due but the payment made is more than what's due
                            | spt, cpt when cpt > spt ->
                                cpt, Overpayment
                            // any other payment made
                            | _, cpt ->
                                cpt, PaymentMade
                    // calculate any charge types incurred
                    let chargeTypes =
                        actualPayments'
                        |> Array.choose(fun ap ->
                            // failed payments that incurred a charge
                            match ap.ActualPaymentStatus with
                            | ActualPaymentStatus.Failed (_, ValueSome chargeType) ->
                                Some chargeType
                            | _ ->
                                None
                        )
                        |> Array.append(
                            // missed payments and underpayments incur a late payment charge
                            match paymentStatus with
                            | MissedPayment | Underpayment ->
                                [| Charge.ChargeType.LatePayment |]
                            | _ ->
                                [||]
                        )
                    // calculate the total of any charges incurred
                    let appliedCharges =
                        chargeTypes
                        |> Array.choose(fun ct ->
                            match chargeConfig with
                            | Some cc when cc.ChargeTypes.ContainsKey ct ->
                                let chargeConditions = cc.ChargeTypes[ct]
                                if chargeHolidays[ct] |> Array.exists((=) offsetDay) then
                                    None
                                else
                                    Some { ChargeType = ct; Total = chargeConditions.Value }
                            | _ ->
                                None
                        )
                    let groupedAppliedCharges =
                        appliedCharges
                        |> Array.groupBy _.ChargeType
                        |> Array.collect(fun (ct, ac) -> 
                            if Array.isEmpty ac then
                                [||]
                            else
                                let chargeConditions = chargeConfig.Value.ChargeTypes[ct]
                                match chargeConditions.ChargeGrouping with
                                | Charge.ChargeGrouping.OneChargeTypePerDay ->
                                    Array.take 1 ac
                                | Charge.ChargeGrouping.OneChargeTypePerSchedule ->
                                    if aggregateAppliedCharges |> Array.exists(fun ac -> ac.ChargeType = ct) then
                                        [||]
                                    else
                                        Array.take 1 ac
                                | Charge.ChargeGrouping.AllChargesApplied ->
                                    ac
                        )
                    // create the applied payment
                    let appliedPayment = {
                        ScheduledPayment = scheduledPayment'
                        ActualPayments = actualPayments'
                        GeneratedPayment = NoGeneratedPayment
                        AppliedCharges = groupedAppliedCharges
                        NetEffect = netEffect
                        PaymentStatus = paymentStatus
                    }
                    let newAggregateAppliedCharges = Array.append appliedCharges aggregateAppliedCharges
                    // add the day to create a key-value pair for mapping
                    (offsetDay, appliedPayment), newAggregateAppliedCharges
                ) Array.empty<AppliedCharge>
                |> fst
                // convert the array to a map
                |> Map.ofArray
            // for settlements or statements, adds a new applied payment or modifies an existing one (generated-payment and payment-status fields)
            let appliedPayments day generatedPayment paymentStatus =
                // if the day is already in the applied payment map, add a placeholder generated payment to the day
                if appliedPaymentMap |> Map.containsKey day then
                    appliedPaymentMap
                    |> Map.map(fun d ap ->
                        if d = day then
                            { ap with GeneratedPayment = generatedPayment }
                        else
                            ap
                    )
                // otherwise, add a new applied payment to the map
                else
                    let newAppliedPayment = {
                        ScheduledPayment = ScheduledPayment.zero
                        ActualPayments = [||]
                        GeneratedPayment = generatedPayment
                        AppliedCharges = [||]
                        NetEffect = 0L<Cent>
                        PaymentStatus = paymentStatus
                    }
                    appliedPaymentMap
                    |> Map.add day newAppliedPayment
            // add or modify the applied payments depending on whether the intended purpose is a settlement or just a statement
            match settlementDay with
                // settlement on a specific day
                | ValueSome (SettlementDay.SettlementOn day) ->
                    appliedPayments day ToBeGenerated Generated
                // settlement on the as-of day
                | ValueSome SettlementDay.SettlementOnAsOfDay ->
                    appliedPayments asOfDay ToBeGenerated Generated
                // statement only
                | ValueNone ->
                    let maxPaymentDay = appliedPaymentMap |> Map.maxKeyValue |> fst
                    // when inspecting after the end of the schedule, just return the schedule with no applied payments added
                    if asOfDay >= maxPaymentDay then
                        appliedPaymentMap
                    // otherwise, add an information-only entry if the payment day is not present
                    else
                        appliedPayments asOfDay NoGeneratedPayment InformationOnly
