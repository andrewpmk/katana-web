using Katana.Models;

namespace Katana.Tests
{
    public class TestLedger
    {
        [Fact]
        public void TestParseFromLedgerLineWithAmount()
        {
            const string line = "   assets:savings    $-6.76 ; note";
            LedgerEntry entry = LedgerEntry.ParseFromLedgerLine(line);

            Assert.NotNull(entry);
            Assert.Equal("assets:savings", entry.AccountName);
            Assert.NotNull(entry.Amount);
            Assert.Equal((decimal)-6.76, (decimal)entry.Amount);
        }

        [Fact]
        public void TestParseFromLedgerLineNoAmount()
        {
            const string line = "   assets:savings    ; note";
            LedgerEntry entry = LedgerEntry.ParseFromLedgerLine(line);

            Assert.NotNull(entry);
            Assert.Equal("assets:savings", entry.AccountName);
            Assert.Null(entry.Amount);
        }

        [Fact]
        public void TestParseLedger()
        {
            const string file =
@" ; note
2023/03/15  Sandwich
    assets:savings                     $-6.76 ; note
    assets:cash                           $-1
    expenses:tips                          $1
    expenses:food:tim-hortons

; note
2023/03/16 Sandwich
    assets:savings                     $-8
    expenses:food:tim-hortons
";

            Ledger ledger = Ledger.ParseJournal(file);

            Assert.NotNull(ledger);
            Assert.Equal(2, ledger.LedgerTransactions.Count);
            Assert.Equal(new DateTime(2023, 03, 15), ledger.LedgerTransactions[0].Date);
            Assert.Equal("Sandwich", ledger.LedgerTransactions[0].Note);

            Assert.Equal("assets:savings", ledger.LedgerTransactions[0].Entries[0].AccountName);
            Assert.Equal((decimal)-6.76, ledger.LedgerTransactions[0].Entries[0].Amount);

            Assert.Equal((decimal)-8, ledger.LedgerTransactions[1].Entries[0].Amount);
            Assert.Null(ledger.LedgerTransactions[1].Entries[1].Amount);
        }

        [Fact]
        public void TestRemoveComment()
        {
            Assert.Equal("blah", Ledger.RemoveComment("blah ; comment"));
            Assert.Equal("blah", Ledger.RemoveComment("blah ;"));
            Assert.Equal("blah", Ledger.RemoveComment("blah; comment"));
            Assert.Equal("blah", Ledger.RemoveComment("blah; comment; second comment"));
            Assert.Equal("blah", Ledger.RemoveComment("blah; "));
            Assert.Equal("blah", Ledger.RemoveComment("blah;"));
            Assert.Equal("", Ledger.RemoveComment(";"));
            Assert.Equal("", Ledger.RemoveComment("; "));
            Assert.Equal("", Ledger.RemoveComment("; comment"));

            Assert.Equal("blah", Ledger.RemoveComment("blah"));
            Assert.Equal("", Ledger.RemoveComment(""));
        }
    }
}