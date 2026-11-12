# Administration and supplier registration

The MVC controllers are in `SourcingTenderSystem/Controllers`, and their Razor views are in `SourcingTenderSystem/Views`. The application uses Entity Framework Core with SQL Server and ASP.NET Core Identity.

To run a local demo, use the .NET 10 SDK and SQL Server:

1. Open `sql/setup-demo.sql` in SQL Server Management Studio and execute it on your local SQL Server. It creates `SourcingTenderSystem_Demo` if needed and applies the project's migrations. You can run it again on the same demo database.
2. In PowerShell, run the following from the solution directory. If you use LocalDB, replace `Server=localhost` with `Server=(localdb)\MSSQLLocalDB`.

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=localhost;Database=SourcingTenderSystem_Demo;Trusted_Connection=True;TrustServerCertificate=True;'
$env:AdminSeed__Email = 'admin@example.test'
$env:AdminSeed__Password = '<shared demo password>'
dotnet run --project SourcingTenderSystem -- --initialize-admin
Remove-Item Env:AdminSeed__Password
dotnet run --project SourcingTenderSystem
```

3. Sign in at `/Account/Login` with the demo credentials. Open `/AdminDashboard` for administration, or sign out and visit `/SupplierRegistration/Register` to create a supplier account. Upload a PDF, sign back in as the administrator, and review the request under **Suppliers**.

The setup command creates the administrator and roles through Identity. If the same administrator already exists, it leaves the password as is. The SQL file contains the database schema only.

For an existing database, use `dotnet ef database update --project SourcingTenderSystem` with an EF CLI that matches the project's EF Core version instead of running the demo SQL. Set the connection string and initial administrator credentials for that environment before running the setup command.

Store `App_Data/SupplierDocuments` in a private, writable directory and back it up with the SQL database. Uploaded PDFs are served through authorized download endpoints.
