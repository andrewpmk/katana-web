using Katana.Data;
using Microsoft.EntityFrameworkCore;

namespace Katana.Models
{
    public class SeedData
    {
        public static async Task Initialize(IServiceProvider serviceProvider)
        {
            using var context = new KatanaContext(serviceProvider.GetRequiredService<DbContextOptions<KatanaContext>>());

            // Bail if we have existing data
            if (await context.Envelopes.AnyAsync() || await context.Accounts.AnyAsync())
                return;

            // Built-in envelopes
            context.Envelopes.Add(new Envelope { Name = "✉️ Available" });
            context.Envelopes.Add(new Envelope { Name = "🍞 Groceries" });

            // Built-in accounts
            //context.Accounts.Add(Account.New("assets:cash"));
            //context.Accounts.Add(Account.New("assets:savings"));
            //context.Accounts.Add(Account.New("credit:visa"));

            await context.SaveChangesAsync();
        }
    }
}
