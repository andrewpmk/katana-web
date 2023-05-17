using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Katana.Data;
using Katana.Models;
using Katana.Store;
using Katana.ViewModels;

namespace Katana.Controllers
{
    public class EnvelopesController : Controller
    {
        private readonly KatanaContext _context;
        private readonly BudgetStore _store;

        public EnvelopesController(KatanaContext context)
        {
            _context = context;
            _store = new BudgetStore(context);
        }

        // GET: Envelopes
        public async Task<IActionResult> Index()
        {
            var envelopes = _context
                .Envelopes
                .OrderBy(env => env.Name)
                .ToList();

            var vm = new List<EnvelopeIndexViewModel>();

            foreach (var envelope in envelopes)
            {
                if (envelope.Id == SpecialEnvelope.Available)
                {
                    vm.Add(new EnvelopeIndexViewModel
                    {
                        Envelope = envelope,
                        AmountInEnvelope = await _store.GetAvailable(),
                        IsAvailableEnvelope = true
                    });
                }
                else
                {
                    decimal spendingTotal = await _store.GetSpendingTotal(envelope);

                    vm.Add(new EnvelopeIndexViewModel
                    {
                        Envelope = envelope,
                        TotalBoundSpending = spendingTotal,
                        AmountInEnvelope = envelope.Amount - spendingTotal
                    });
                }
            }

            return View(vm);
        }


        // GET: Envelopes/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var envelope = await _store.GetEnvelope((int)id);
            if (envelope == null)
                return NotFound();
                        
            List<Account> boundAccounts = await _store.GetBoundAccounts(envelope);

            List<Transaction> allTransactions =
                _context.Transactions
                        .Include(trans => trans.Entries)
                        .ThenInclude(entry => entry.Account)
                        .ToList();

            // Create the rows for the Spending section
            // Get the transactions with at least one entry whose account is bound to this envelope
            List<Transaction> transactions =
                allTransactions
                    .Where(trans => trans.Entries.Any(entry => entry.Account.BoundTo == envelope))
                    .OrderByDescending(t => t.Date)
                    .ThenByDescending(t => t.Id)
                    .ToList();

            List<Stash> stashes =
                _context.Stashes
                        .Include(s => s.From)
                        .Include(s => s.To)
                        .Where(stash => stash.To == envelope || stash.From == envelope)
                        .OrderByDescending(stash => stash.Date)
                        .ToList();

            decimal netStashed =
                stashes.Sum(stash => (stash.To == envelope ? 1 : -1) * stash.Amount);

            decimal spendingTotal =
                allTransactions
                    .SelectMany(trans => trans.Entries)
                    .Where(entry => entry.Account.BoundTo == envelope)
                    // TODO: should this be only Amount > 0 as in other places?
                    // ie how to handle refunds on bound accounts
                    .Sum(entry => entry.Amount);

            decimal funds = netStashed - spendingTotal;

            var vm = new EnvelopeDetailsViewModel
            {
                Envelope = envelope,
                BoundAccounts = boundAccounts,
                Transactions = transactions,
                NetStashed = netStashed,
                SpendingTotal = spendingTotal,
                FundsInEnvelope = funds,
                Stashes = stashes,
                IsUnfundedSpending = funds < 0
            };
            
            if (envelope.Id == SpecialEnvelope.Available)
            {
                // total up the inflow for the Available envelope
                List<EntryWithDateAndNote> inflowEntries =
                    allTransactions
                        .SelectMany(trans => trans.Entries
                                                  .Where(e => e.Account.Name.StartsWith("assets")
                                                           && e.Amount > 0)
                                                  .Select(entry => new EntryWithDateAndNote
                                                                   {
                                                                       Date = trans.Date,
                                                                       Entry = entry,
                                                                       Note = trans.Note
                                                                   }))
                        .OrderByDescending(entry => entry.Date)
                        .ToList();

                decimal inflowTotal = inflowEntries.Sum(entry => entry.Entry.Amount);
                
                vm.TotalInflow = inflowTotal;
                vm.InflowEntries = inflowEntries;
                vm.IsAvailableEnvelope = true;
                vm.AvailableFunds = vm.TotalInflow + vm.NetStashed;
            }

            return View(vm);
        }

        // GET: Envelopes/Stash
        public async Task<IActionResult> Stash()
        {
            var envelopes = await _context.Envelopes
                                    .Where(env => env.Id != SpecialEnvelope.Available)
                                    .OrderBy(env => env.Name)
                                    .ToListAsync();

            var allTransactions = _context.Transactions
                                          .Include(t => t.Entries)
                                          .ThenInclude(e => e.Account);

            var rows = new List<StashRow>();

            foreach (var envelope in envelopes)
            {
                decimal netStashes =
                    await _context.Stashes
                            .Where(stash => stash.To == envelope || stash.From == envelope)
                            .SumAsync(stash => (stash.To == envelope ? 1 : -1) * stash.Amount);

                var row = new StashRow
                {
                    Envelope = envelope,

                    // all stashes to/from this envelope
                    NewAmount = netStashes,

                    // this comes back populated by the user if they stashed anything in this envelope
                    Stashing = 0
                };

                rows.Add(row);
            }

            decimal total_stash = rows.Sum(r => r.NewAmount);

            // total up the inflow
            decimal inflow =
                await allTransactions
                    .SelectMany(t => t.Entries)
                    .Where(e => e.Account.Name.StartsWith("assets"))
                    .Where(e => e.Amount > 0)
                    .SumAsync(e => e.Amount);

            var vm = new StashViewModel
            {
                Available = inflow - total_stash,
                Rows = rows
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessStash(StashViewModel vm)
        {
            var envelopes = _context.Envelopes.ToDictionary(e => e.Id);

            if (ModelState.IsValid)
            {
                DateTime now = DateTime.Now;
                foreach (var envelope in vm.Rows.Where(r => r.Stashing != 0))
                {
                    await _store.Stash(new Stash
                    {
                        From = envelopes[1],
                        To = envelopes[envelope.Envelope.Id],
                        Date = now,
                        Amount = envelope.Stashing
                    });
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Details", "Envelopes", new { id = 1 });
        }

        // GET: Envelopes/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Envelopes/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Name")] Envelope envelope)
        {
            if (ModelState.IsValid)
            {
                await _context.AddAsync(envelope);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(envelope);
        }

        // GET: Envelopes/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Envelopes == null)
                return NotFound();

            var envelope = await _store.GetEnvelope((int)id);
            if (envelope == null)
                return NotFound();

            return View(envelope);
        }

        // POST: Envelopes/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,HexColor")] Envelope envelope)
        {
            if (id != envelope.Id)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var there = await _store.GetEnvelope(id);
                    there.Name = envelope.Name;
                    there.HexColor = envelope.HexColor;
                                        
                    _context.SaveChanges();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await EnvelopeExists(envelope.Id))
                        return NotFound();
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(envelope);
        }

        // GET: Envelopes/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || _context.Envelopes == null)
                return NotFound();

            var envelope = await _store.GetEnvelope((int)id);
            if (envelope == null)
                return NotFound();
            
            return View(envelope);
        }

        // POST: Envelopes/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.Envelopes == null)
                return Problem("Entity set 'KatanaContext.Envelope' is null.");

            var envelope = await _store.GetEnvelope(id);
            if (envelope != null)
                _context.Envelopes.Remove(envelope);
            
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private async Task<bool> EnvelopeExists(int id)
        {
            return await _context.Envelopes?.AnyAsync(e => e.Id == id);
        }
    }
}
