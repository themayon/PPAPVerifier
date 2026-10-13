using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Domain;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Domain;

public sealed class CreditAccountTests
{
    [Fact]
    public void Create_Should_InitializeTenantOwnerAndZeroBalances_When_InputIsValid()
    {
        Guid ownerUserId = Guid.NewGuid();
        DateTime before = DateTime.UtcNow;

        CreditAccount account = CreditAccount.Create(" tenant-1 ", ownerUserId);

        account.Id.ShouldNotBe(Guid.Empty);
        account.TenantId.ShouldBe("tenant-1");
        account.OwnerUserId.ShouldBe(ownerUserId);
        account.AvailableCredits.ShouldBe(0);
        account.ReservedCredits.ShouldBe(0);
        account.Version.ShouldBe(0);
        account.CreatedAtUtc.ShouldBeInRange(before, DateTime.UtcNow);
        account.UpdatedAtUtc.ShouldBeNull();
        account.Transactions.ShouldBeEmpty();
    }

    [Fact]
    public void TopUp_Should_IncreaseAvailableBalanceAndAppendTransaction_When_ApprovedAmountIsPositive()
    {
        CreditAccount account = CreateAccount();

        CreditTransaction transaction = account.TopUp(25, " topup-1 ");

        account.AvailableCredits.ShouldBe(25);
        account.ReservedCredits.ShouldBe(0);
        account.Version.ShouldBe(1);
        account.UpdatedAtUtc.ShouldBe(transaction.CreatedAtUtc);
        account.Transactions.ShouldBe([transaction]);
        transaction.Id.ShouldNotBe(Guid.Empty);
        transaction.TenantId.ShouldBe(account.TenantId);
        transaction.CreditAccountId.ShouldBe(account.Id);
        transaction.Kind.ShouldBe(CreditTransactionKind.Topup);
        transaction.Amount.ShouldBe(25);
        transaction.Reference.ShouldBe("topup-1");
        transaction.CreatedAtUtc.ShouldNotBe(default);
    }

    [Fact]
    public void Reserve_Should_MoveCreditsFromAvailableToReserved_When_BalanceIsSufficient()
    {
        CreditAccount account = CreateFundedAccount(20);

        CreditTransaction transaction = account.Reserve(10, "submission-1");

        account.AvailableCredits.ShouldBe(10);
        account.ReservedCredits.ShouldBe(10);
        account.Version.ShouldBe(2);
        transaction.Kind.ShouldBe(CreditTransactionKind.Reserve);
        transaction.Amount.ShouldBe(10);
    }

    [Fact]
    public void CommitReservation_Should_DecreaseReservedBalance_When_ReservationExists()
    {
        CreditAccount account = CreateFundedAccount(20);
        account.Reserve(10, "submission-1");

        CreditTransaction transaction = account.CommitReservation(10, "submission-1");

        account.AvailableCredits.ShouldBe(10);
        account.ReservedCredits.ShouldBe(0);
        account.Version.ShouldBe(3);
        transaction.Kind.ShouldBe(CreditTransactionKind.Commit);
        account.Transactions.Select(x => x.Kind).ShouldBe([
            CreditTransactionKind.Topup,
            CreditTransactionKind.Reserve,
            CreditTransactionKind.Commit]);
    }

    [Fact]
    public void ReleaseReservation_Should_ReturnReservedCreditsToAvailable_When_ReservationExists()
    {
        CreditAccount account = CreateFundedAccount(20);
        account.Reserve(10, "submission-1");

        CreditTransaction transaction = account.ReleaseReservation(10, "submission-1");

        account.AvailableCredits.ShouldBe(20);
        account.ReservedCredits.ShouldBe(0);
        account.Version.ShouldBe(3);
        transaction.Kind.ShouldBe(CreditTransactionKind.Release);
    }

    [Fact]
    public void Refund_Should_IncreaseAvailableBalanceAndAppendTransaction_When_AmountIsPositive()
    {
        CreditAccount account = CreateFundedAccount(20);
        account.Reserve(10, "submission-1");
        account.CommitReservation(10, "submission-1");

        CreditTransaction transaction = account.Refund(10, "submission-1");

        account.AvailableCredits.ShouldBe(20);
        account.ReservedCredits.ShouldBe(0);
        account.Version.ShouldBe(4);
        transaction.Kind.ShouldBe(CreditTransactionKind.Refund);
    }

    [Fact]
    public void Reserve_Should_ThrowConflictAndPreserveBalances_When_AvailableBalanceIsInsufficient()
    {
        CreditAccount account = CreateFundedAccount(9);

        CustomException exception = Should.Throw<CustomException>(() => account.Reserve(10, "submission-1"));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        account.AvailableCredits.ShouldBe(9);
        account.ReservedCredits.ShouldBe(0);
        account.Version.ShouldBe(1);
        account.Transactions.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(CreditTransactionKind.Commit)]
    [InlineData(CreditTransactionKind.Release)]
    public void ReservationDebit_Should_ThrowConflictAndPreserveBalances_When_ReservedBalanceIsInsufficient(
        CreditTransactionKind kind)
    {
        CreditAccount account = CreateFundedAccount(20);
        account.Reserve(5, "submission-1");

        CustomException exception = Should.Throw<CustomException>(() =>
            ApplyReservationDebit(account, kind, 6, "submission-2"));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        account.AvailableCredits.ShouldBe(15);
        account.ReservedCredits.ShouldBe(5);
        account.Version.ShouldBe(2);
        account.Transactions.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(CreditTransactionKind.Topup)]
    [InlineData(CreditTransactionKind.Reserve)]
    [InlineData(CreditTransactionKind.Commit)]
    [InlineData(CreditTransactionKind.Release)]
    [InlineData(CreditTransactionKind.Refund)]
    public void Operation_Should_ReturnExistingTransactionWithoutMutation_When_ExactOperationIsReplayed(
        CreditTransactionKind kind)
    {
        CreditAccount account = CreateFundedAccount(50);
        if (kind is CreditTransactionKind.Commit or CreditTransactionKind.Release)
        {
            account.Reserve(10, $"reserve-{kind}");
        }
        CreditTransaction first = Apply(account, kind, 10, "operation-1");
        int availableCredits = account.AvailableCredits;
        int reservedCredits = account.ReservedCredits;
        int version = account.Version;
        DateTime? updatedAtUtc = account.UpdatedAtUtc;
        int transactionCount = account.Transactions.Count;

        CreditTransaction replay = Apply(account, kind, 10, " operation-1 ");

        replay.ShouldBeSameAs(first);
        account.AvailableCredits.ShouldBe(availableCredits);
        account.ReservedCredits.ShouldBe(reservedCredits);
        account.Version.ShouldBe(version);
        account.UpdatedAtUtc.ShouldBe(updatedAtUtc);
        account.Transactions.Count.ShouldBe(transactionCount);
    }

    [Theory]
    [InlineData(CreditTransactionKind.Topup)]
    [InlineData(CreditTransactionKind.Reserve)]
    [InlineData(CreditTransactionKind.Commit)]
    [InlineData(CreditTransactionKind.Release)]
    [InlineData(CreditTransactionKind.Refund)]
    public void Operation_Should_ThrowConflictWithoutMutation_When_ReferenceIsReusedWithDifferentAmount(
        CreditTransactionKind kind)
    {
        CreditAccount account = CreateFundedAccount(50);
        if (kind is CreditTransactionKind.Commit or CreditTransactionKind.Release)
        {
            account.Reserve(20, $"reserve-{kind}");
        }
        Apply(account, kind, 10, "operation-1");
        int availableCredits = account.AvailableCredits;
        int reservedCredits = account.ReservedCredits;
        int version = account.Version;
        int transactionCount = account.Transactions.Count;

        CustomException exception = Should.Throw<CustomException>(() =>
            Apply(account, kind, 11, "operation-1"));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        account.AvailableCredits.ShouldBe(availableCredits);
        account.ReservedCredits.ShouldBe(reservedCredits);
        account.Version.ShouldBe(version);
        account.Transactions.Count.ShouldBe(transactionCount);
    }

    [Theory]
    [InlineData(CreditTransactionKind.Topup, 0)]
    [InlineData(CreditTransactionKind.Reserve, -1)]
    [InlineData(CreditTransactionKind.Commit, 0)]
    [InlineData(CreditTransactionKind.Release, -1)]
    [InlineData(CreditTransactionKind.Refund, 0)]
    public void Operation_Should_RejectNonPositiveAmountWithoutMutation_When_AmountIsInvalid(
        CreditTransactionKind kind,
        int amount)
    {
        CreditAccount account = CreateFundedAccount(50);

        Should.Throw<ArgumentOutOfRangeException>(() => Apply(account, kind, amount, "operation-1"));

        account.AvailableCredits.ShouldBe(50);
        account.ReservedCredits.ShouldBe(0);
        account.Version.ShouldBe(1);
        account.Transactions.Count.ShouldBe(1);
    }

    [Fact]
    public void TopUp_Should_ThrowOverflowAndPreserveBalance_When_ResultExceedsIntegerRange()
    {
        CreditAccount account = CreateAccount();
        account.TopUp(int.MaxValue, "topup-1");

        Should.Throw<OverflowException>(() => account.TopUp(1, "topup-2"));

        account.AvailableCredits.ShouldBe(int.MaxValue);
        account.ReservedCredits.ShouldBe(0);
        account.Version.ShouldBe(1);
        account.Transactions.Count.ShouldBe(1);
    }

    private static CreditAccount CreateAccount() =>
        CreditAccount.Create("tenant-1", Guid.NewGuid());

    private static CreditAccount CreateFundedAccount(int amount)
    {
        CreditAccount account = CreateAccount();
        account.TopUp(amount, "initial-topup");
        return account;
    }

    private static CreditTransaction Apply(
        CreditAccount account,
        CreditTransactionKind kind,
        int amount,
        string reference) =>
        kind switch
        {
            CreditTransactionKind.Topup => account.TopUp(amount, reference),
            CreditTransactionKind.Reserve => account.Reserve(amount, reference),
            CreditTransactionKind.Commit => account.CommitReservation(amount, reference),
            CreditTransactionKind.Release => account.ReleaseReservation(amount, reference),
            CreditTransactionKind.Refund => account.Refund(amount, reference),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static CreditTransaction ApplyReservationDebit(
        CreditAccount account,
        CreditTransactionKind kind,
        int amount,
        string reference) =>
        kind switch
        {
            CreditTransactionKind.Commit => account.CommitReservation(amount, reference),
            CreditTransactionKind.Release => account.ReleaseReservation(amount, reference),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
}
