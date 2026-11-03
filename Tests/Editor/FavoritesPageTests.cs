using System.Linq;
using NUnit.Framework;

namespace FavoritesBar.Tests
{
    public class FavoritesPageTests
    {
        static FavoritesStore.Page PageWith(params string[] ids)
        {
            var page = new FavoritesStore.Page();
            foreach (var id in ids) page.items.Add(new FavoritesStore.Item { id = id });
            return page;
        }

        static string Ids(FavoritesStore.Page page) => string.Join(",", page.items.Select(x => x.id));

        [Test]
        public void Insert_AddsAtIndex()
        {
            var page = PageWith("a", "b");
            page.Insert(new FavoritesStore.Item { id = "c" }, 1);
            Assert.AreEqual("a,c,b", Ids(page));
        }

        [Test]
        public void Insert_ClampsIndex()
        {
            var page = PageWith("a");
            page.Insert(new FavoritesStore.Item { id = "b" }, 99);
            page.Insert(new FavoritesStore.Item { id = "c" }, -5);
            Assert.AreEqual("c,a,b", Ids(page));
        }

        [Test]
        public void Insert_ExistingItemMovesInsteadOfDuplicating()
        {
            var page = PageWith("a", "b", "c");
            page.Insert(new FavoritesStore.Item { id = "a" }, 3);
            Assert.AreEqual("b,c,a", Ids(page));
            page.Insert(new FavoritesStore.Item { id = "a" }, 0);
            Assert.AreEqual("a,b,c", Ids(page));
        }

        [Test]
        public void Insert_DroppingOnItsOwnSlotKeepsOrder()
        {
            var page = PageWith("a", "b", "c");
            page.Insert(page.items[1], 2); // drop "b" just below itself
            Assert.AreEqual("a,b,c", Ids(page));
        }
    }
}
