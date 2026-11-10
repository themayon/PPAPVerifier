using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Domain;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Domain;

public sealed class PpapSupplierTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);

    private static PpapSupplier Create(PpapSupplierProfile? profile = null, IEnumerable<Guid>? users = null, IEnumerable<PpapSupplierCertificateInput>? certificates = null, string code = "VND-000001") =>
        PpapSupplier.Create("acme", code, profile ?? new PpapSupplierProfile("Supplier"), users ?? [], certificates ?? [], Now);

    [Fact]
    public void Create_Should_NormalizeProfile_AndLinkDistinctUsers()
    {
        Guid user = Guid.NewGuid();
        PpapSupplier supplier = Create(
            new PpapSupplierProfile(" Global Tech GmbH ", " DE123456789 ", "12-345-6789", " Main St 1 ", " Munich ", " Germany ", " Plant 2 ", " Site St 9 ", false, " Jane ", " QA Manager ", " jane@supplier.com ", " +49 1 "),
            [user, user, Guid.Empty],
            code: " vnd-000001 ");

        supplier.Code.ShouldBe("VND-000001");
        supplier.Name.ShouldBe("Global Tech GmbH");
        supplier.TaxId.ShouldBe("DE123456789");
        supplier.DunsNumber.ShouldBe("123456789");
        supplier.City.ShouldBe("Munich");
        supplier.SiteAddress.ShouldBe("Site St 9");
        supplier.ContactJobTitle.ShouldBe("QA Manager");
        supplier.Email.ShouldBe("jane@supplier.com");
        supplier.IsActive.ShouldBeTrue();
        supplier.Users.Select(x => x.UserId).ShouldBe([user]);
    }

    [Fact]
    public void Create_Should_DropSiteAddress_WhenSameAsHeadquarters()
    {
        PpapSupplier supplier = Create(new PpapSupplierProfile("Supplier", Address: "Main St", SiteName: "Plant 1", SiteAddress: "Elsewhere", SiteSameAsHeadquarters: true));

        supplier.SiteSameAsHeadquarters.ShouldBeTrue();
        supplier.SiteName.ShouldBe("Plant 1");
        supplier.SiteAddress.ShouldBeNull();
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("12-345-678X")]
    public void Create_Should_RejectInvalidDuns(string duns)
    {
        Should.Throw<ArgumentException>(() => Create(new PpapSupplierProfile("Supplier", DunsNumber: duns)));
    }

    [Fact]
    public void Update_Should_ReplaceUsersAndCertificates_AndDeactivate()
    {
        Guid kept = Guid.NewGuid(); Guid removed = Guid.NewGuid(); Guid added = Guid.NewGuid();
        Guid iso = Guid.NewGuid(); Guid iatf = Guid.NewGuid();
        PpapSupplier supplier = Create(users: [kept, removed], certificates: [new(iso, "iso.pdf", "ISO 9001")]);

        supplier.Update(1, "VND-000001", new PpapSupplierProfile("Supplier"), false, [kept, added], [new(iso, "iso.pdf", "iso 14001"), new(iatf, "iatf.pdf", "IATF 16949")], Now);

        supplier.IsActive.ShouldBeFalse();
        supplier.Users.Select(x => x.UserId).ShouldBe([kept, added], ignoreOrder: true);
        supplier.Certificates.Select(x => (x.FileAssetId, x.CertificateType)).ShouldBe([(iso, "ISO 14001"), (iatf, "IATF 16949")], ignoreOrder: true);
        supplier.Version.ShouldBe(2);
    }

    [Fact]
    public void Create_Should_RejectUnknownCertificateType()
    {
        Should.Throw<ArgumentException>(() => Create(certificates: [new(Guid.NewGuid(), "x.pdf", "ISO 1234")]));
    }

    [Theory]
    [InlineData("bad code")]
    [InlineData("")]
    public void Create_Should_RejectInvalidCode(string code)
    {
        Should.Throw<ArgumentException>(() => Create(code: code));
    }

    [Fact]
    public void Create_Should_RejectInvalidEmail()
    {
        Should.Throw<ArgumentException>(() => Create(new PpapSupplierProfile("Supplier", Email: "not-an-email")));
    }

    [Fact]
    public void Update_Should_RejectStaleVersion()
    {
        PpapSupplier supplier = Create();
        Should.Throw<CustomException>(() => supplier.Update(3, "VND-000001", new PpapSupplierProfile("Supplier"), true, [], [], Now));
    }

    [Fact]
    public void Part_Should_StoreSupplier_OnCreateAndUpdate()
    {
        Guid supplier = Guid.NewGuid(); Guid other = Guid.NewGuid();
        PpapPart part = PpapPart.Create("acme", Guid.NewGuid(), "Part", "PN-1", "A", supplier);
        part.SupplierId.ShouldBe(supplier);

        part.UpdateDetails("Part", "PN-1", "A", other, 1);
        part.SupplierId.ShouldBe(other);
        part.UpdateDetails("Part", "PN-1", "A", 2);
        part.SupplierId.ShouldBe(other);
    }
}
