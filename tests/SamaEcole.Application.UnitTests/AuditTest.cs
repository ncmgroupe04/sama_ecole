using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Persistence;
using SamaEcole.Domain.Entities;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Tools
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting test...");
            // Just simulate what AuditLoggingBehavior does.
            // But we need the DB connection.
            // Wait, we can just run the test project.
        }
    }
}
