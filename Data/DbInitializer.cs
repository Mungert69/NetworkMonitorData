



using System;

namespace NetworkMonitor.Data
{
    public static class DbInitializer
    {
        public static void Initialize(MonitorContext context)
        {
            try
            {
                context.Database.EnsureCreated();
                context.SaveChanges();
            }
            catch (Exception e)
            {
                Console.WriteLine("Error occured in DBInitlaizer : Error Was : " + e.Message);
            }



        }
    }
}
