using Iris.Core.Networking.Fake;

namespace Iris.Tests;

public class FakeDbTests
{
    [Fact]
    public void A_file_saved_before_one_account_per_church_is_reseeded()
    {
        var path = Path.Combine(Path.GetTempPath(), $"iris-fake-{Guid.NewGuid():N}.json");
        try
        {
            // The old shape: memberships with roles, no schema number.
            File.WriteAllText(path, "{\"seeded\":true,\"users\":[{\"email\":\"pastor@vidanueva.org\",\"memberships\":[{\"role\":\"owner\"}]}],\"churches\":[]}");

            var db = new FakeDbStore(path).Load();

            Assert.Equal(FakeDb.CurrentSchema, db.Schema);
            var pastor = Assert.Single(db.Users, u => u.Email == "pastor@vidanueva.org");
            Assert.Equal(FakeSeed.VidaNuevaId, pastor.ChurchId);
            Assert.False(db.SystemBibleEnabled);

            // Saved again in the current shape: the next start keeps it.
            Assert.Equal(FakeDb.CurrentSchema, new FakeDbStore(path).Load().Schema);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
