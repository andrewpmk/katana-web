using Katana.Data;
using Katana.Models;
using Microsoft.EntityFrameworkCore;

namespace Katana.Store
{
    public interface IBudgetStore
    {
        /* Accounts */

        Task<Account?> GetAccount(int id);
        Task<List<Account>> GetBoundAccounts(Envelope envelope);


        /* Transactions */

        Task<Transaction?> GetTransaction(int id);

        // Get all transactions that have at least one entry involving this account
        Task<List<Transaction>> GetTransactionsWithAccount(Account account);


        /* Envelopes */

        Task<Envelope?> GetEnvelope(int id);

        Task Stash(Stash stash);


        /* Reports */

        Task<Dictionary<Account, decimal>> GetAccountBalances();
        Task<decimal> GetAvailable();
        Task<decimal> GetSpendingTotal(Envelope envelope);
    }


    public class BudgetStore : IBudgetStore
    {
        private readonly KatanaContext _context;

        public BudgetStore(KatanaContext context) => _context = context;

        #region Accounts

        public async Task<Account?> GetAccount(int id)
        {
            return await _context
                .Accounts
                .Include(a => a.BoundTo)
                .FirstOrDefaultAsync(a => a.Id == id);
        }

        /// <summary>
        /// Get accounts bound to this envelope
        /// </summary>
        /// <param name="envelope"></param>
        /// <returns></returns>
        public async Task<List<Account>> GetBoundAccounts(Envelope envelope)
        {
            return await _context.Accounts
                           .Where(a => a.BoundTo == envelope)
                           .ToListAsync();
        }

        #endregion
        #region Transactions

        /// <summary>
        /// Get an individual transaction, including its entries and bound accounts
        /// </summary>
        /// <param name="id">The Transaction.Id for the transaction</param>
        public async Task<Transaction?> GetTransaction(int id)
        {
            return await _context.Transactions
                           .Include(t => t.Entries)
                           .ThenInclude(e => e.Account)
                           .Where(t => t.Id == id)
                           .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Get a list of transactions with at least one entry from this account
        /// </summary>
        public async Task<List<Transaction>> GetTransactionsWithAccount(Account account)
        {
            return await _context.Transactions
                           .Include(trans => trans.Entries)
                           .ThenInclude(entry => entry.Account)
                           .Where(t => t.Entries.Any(e => e.Account == account))
                           .OrderBy(t => t.Date)
                           .ToListAsync();
        }

        #endregion
        #region Envelopes

        public async Task<Envelope?> GetEnvelope(int id) => await _context.Envelopes.FirstOrDefaultAsync(e => e.Id == id);

        #endregion
        #region Reports

        public async Task<Dictionary<Account, decimal>> GetAccountBalances()
        {
            var accountBalances = await _context.Transactions
                .SelectMany(t => t.Entries)
                .GroupBy(entry => entry.Account.Id)
                .ToDictionaryAsync(group => group.Key,
                              group => group.Sum(entry => entry.Amount));

            var accounts = await _context.Accounts
                .ToDictionaryAsync(account => account.Id);

            return accountBalances
                .ToDictionary(kvp => accounts[kvp.Key],
                              kvp => kvp.Value);
        }

        /// <summary>
        /// Move an amount from one Envelope to another, updating the .Amount field for each accordingly
        /// </summary>
        public async Task Stash(Stash stash)
        {
            await _context.Stashes.AddAsync(stash);

            var from = await _context.Envelopes.FindAsync(stash.From.Id);
            var to   = await _context.Envelopes.FindAsync(stash.To.Id);

            // TODO: can we do this??
            from.Amount -= stash.Amount;
            to.Amount += stash.Amount;
        }

        /// <summary>
        /// Get the amount of funds sitting in the Available envelope. This is the sum of positive inflows
        /// to accounts starting with "assets" plus the net amount stashed into the Available envelope
        /// </summary>
        /// <returns></returns>
        public async Task<decimal> GetAvailable()
        {
            // total up the inflow for the Available envelope
            decimal inflow = await _context
                .Transactions
                .Include(t => t.Entries)
                .ThenInclude(e => e.Account)
                .SelectMany(t => t.Entries)
                .Where(e => e.Account.Name.StartsWith("assets")

                            /* This isn't correct, we need to see the other side of this
                             * transaction but that info is gone by this point. And sometimes
                             * can't even be gotten anyway ("split source" transactions?)
                             * 
                             * && e.Entry.Account.BoundTo == null */)

                .Where(e => e.Amount > 0)
                .SumAsync(e => e.Amount);

            decimal netStashed =
                await _context.Stashes
                        .Where(stash => stash.To.Id   == SpecialEnvelope.Available
                                     || stash.From.Id == SpecialEnvelope.Available)
                        .SumAsync(stash => (stash.To.Id == SpecialEnvelope.Available ? 1 : -1) * stash.Amount);

            return inflow + netStashed;
        }


        /// <summary>
        /// Return the sum of positive amounts of all entries bound to this account
        /// </summary>
        public async Task<decimal> GetSpendingTotal(Envelope envelope)
        {
            return await _context.Transactions
                           .Include(t => t.Entries)
                           .ThenInclude(e => e.Account)
                           .SelectMany(t => t.Entries)
                           .Where(entry => entry.Account.BoundTo == envelope)
                           .Where(entry => entry.Amount > 0)
                           .SumAsync(entry => entry.Amount);
        }

        #endregion
    }
}
