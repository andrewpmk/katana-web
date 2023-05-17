using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Katana.Data;
using Katana.Models;
using Katana.Store;
using Katana.ViewModels;

namespace Katana.Controllers
{
    public class TransactionsController : Controller
    {
        private readonly KatanaContext _context;
        private readonly BudgetStore _store;

        public TransactionsController(KatanaContext context)
        {
            _context = context;
            _store = new BudgetStore(context);
        }

        // GET: Transactions
        public async Task<IActionResult> Index()
        {
            return View(await _context.Transactions
                                .Include(t => t.Entries)
                                .OrderByDescending(t => t.Date)
                                .ThenByDescending(t => t.Id)
                                .Take(100) // TODO: 100 limit
                                .ToListAsync());
        }

        // GET: Transactions/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || _context.Transactions == null)
                return NotFound();
            
            var transaction = await _store.GetTransaction((int)id);
            if (transaction == null)
                return NotFound();

            return View(transaction);
        }

        // GET: Transactions/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Transactions/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Note,Date")] Transaction transaction)
        {
            if (ModelState.IsValid)
            {
                _context.Add(transaction);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(transaction);
        }

        // GET: Transactions/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Transactions == null)
                return NotFound();

            var transaction = await _store.GetTransaction((int)id);
            if (transaction == null)
                return NotFound();

            return View(transaction);
        }

        // POST: Transactions/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Note,Date")] Transaction transaction)
        {
            if (id != transaction.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(transaction);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await TransactionExists(transaction.Id))
                    {
                        return NotFound();
                    }

                    throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(transaction);
        }

        // GET: Transactions/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || _context.Transactions == null)
            {
                return NotFound();
            }

            var transaction = await _context.Transactions
                .FirstOrDefaultAsync(m => m.Id == id);
            if (transaction == null)
            {
                return NotFound();
            }

            return View(transaction);
        }

        // POST: Transactions/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.Transactions == null)
            {
                return Problem("Entity set 'KatanaContext.Transactions'  is null.");
            }
            var transaction = await _context.Transactions.FindAsync(id);
            if (transaction != null)
            {
                _context.Transactions.Remove(transaction);
            }
            
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private async Task<bool> TransactionExists(int id)
        {
            return await _context.Transactions?.AnyAsync(e => e.Id == id);
        }

        // GET: Transactions/Import
        public IActionResult Import()
        {
            var vm = new TransactionImportViewModel();
            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> ProcessImport(TransactionImportViewModel vm)
        {
            var journal = Ledger.ParseJournal(vm.LedgerText.Trim());

            journal.LedgerTransactions
                   .ForEach(Ledger.Balance); // balance them or die trying

            // pull all the accounts over right away. add to them as needed
            List<Account> accounts = await _context.Accounts.ToListAsync();
                        
            foreach (var lt in journal.LedgerTransactions)
            {
                Transaction transaction = new()
                {
                    Date = lt.Date,
                    Note = lt.Note
                };

                foreach (LedgerEntry le in lt.Entries)
                {
                    // check first if this account is in our local list. if not, start a new
                    // one and save it to both the local list and the database context
                    Account account = accounts.FirstOrDefault(a => a.Name == le.AccountName);

                    if (account == null)
                    {
                        account = Account.New(le.AccountName);
                        accounts.Add(account);
                        _context.Accounts.Add(account);
                    }

                    Entry entry = new() {
                        Account = account,
                        Amount = (decimal)le.Amount
                    };

                    transaction.Entries.Add(entry);
                }
                _context.Transactions.Add(transaction);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }
    }
}
