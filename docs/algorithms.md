---
title: Algorithms in depth
category: Technical
categoryindex: 3
index: 2
description: An in-depth look at the algorithms used to create amortisation schedules
keywords: algorithm methodology amortisation amortization
---

# Algorithms in Depth

## Calculating the initial schedule and payments

```Amortisation.generate```

Generating the amortisation starts by calling:

```Scheduling.calculate```

As we're looking at an initial schedule, we don't need all the complexity of the amortisation schedule, because we don't need to look into
things like actual payments, late fees, refunds, etc. We just need to create a simple schedule that's going to let us calculate the payments
necessary to bring the final principal balance to zero.

There is no formula to calculate this as it requires recursion - the interest due depends on the principal balance, the amount you pay affects
how much interest is paid off (this is paid off first) and the remainder goes towards the principal, and the remaining principal determines
how much interest is due on the next payment.

So we start with a rough payment: ```(principal + interest) / paymentCount```

> NB: principal here lumps both principal and fees together for simplicity

Then we look at the final principal balance generated by this schedule, adjust the rough payment accordingly, and run the calculation again
as many times as necessary until the final principal balance is at or just below zero (assuming a lower final payment is required).

> ```Array.solveBisection``` is a solver function similar to the one in Excel that is optimised for calculating level payments.

> ```Array.solveNewtonRaphson``` is an alternative implementation of the solver function that is optimised for calculating APRs.

### Simple (or actuarial) interest method

For the simple method, once a solution is found, we have our initial payment schedule detailing the days and amounts to be paid, plus a few statistics
based on this information.

### Add-on interest method

For the add-on method, we run the solver a second time, taking the total interest from the simple method and using it as the initial interest balance
for the new calculation. However, with a non-zero initial interest balance, bearing in mind that payments are applied to interest balances first,
this has the effect of maintaining a higher principal balance for longer, and therefore the final principal balance is again non-zero. So we have
to adjust the payments to compensate for this, and recursively generate the schedule until the final balance is just below zero again. This gives
us our level payments followed by an equal or slightly lower final payment.

We now have our initial payment schedule detailing the days and amounts to be paid, plus a few statistics based on this information.

### Interest caps

The only other consideration in generating this initial schedule is to respect any interest caps imposed, both daily and total. For this reason
we maintain aggregate interest limits and aggregate interest amounts and compare them throughout the generation process, capping the interest where
necessary.

## Applying actual payments to the initial schedule

```AppliedPayment.applyPayments```

Here, we take our initial scheduled payments plus any actual payments made, and group them by day, and create a payment status based on the
relative timings and amounts. It also applies late payment charges where necessary. It also marks any days for which a settlement figure will
need to be generated, or, for statements, it inserts an entry for the as-of day into the schedule (if it isn't already there) to enable an exact
balance for the as-of day to be calculated.

> NB: the applied payments stage is an intermediate calculation stage and most of the finalised calculations are performed in the next stage

## Creating the amortisation schedule

```Amortisation.calculate``` (internal)

When creating the amortisation schedule, we tie all of this information together, applying the full range of parameters to the schedule.

> There are a lot of variables and the order of calculation is critical, which is what makes F# such a good choice for implementing this. Some
variables are maintained in their original and modified form (typically suffixed by an apostrophe or two to make it easy to track the modifications),
and either form may be necessary throughout the algorithm. It is therefore recommended to examine the code itself if you need to see the details.

By way of overview though, the following are performed:

- Payments made are applied first to penalty charges, then interest, then product fees and finally principal.

- Interest caps are checked again, grace periods applied and any product fee refunds calculated.

- Interest is kept as a decimal value until it is apportioned, at which point it is rounded (typically downwards) to the nearest whole `<Cent>`.

- If there is any generated settlement figure to be calculated, this is done here.

- Payment statuses are based on payment windows, which are basically the intervals between successive scheduled payments from the original schedule.
If a payment is not paid in full, we look at whether the balance of the payment is paid later in the window, in which case it would just be a late
payment rather than a missed payment (with different consequences for credit reporting).

- Lastly, statistics are calculated to show the figures based on the final balances.
