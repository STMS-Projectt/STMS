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

### Sourcing demo accounts

After applying the database schema, run from the solution directory with the
same development connection string used by the app:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:SourcingDemo__Password = '<choose a password that meets Identity requirements>'
dotnet run --project SourcingTenderSystem -- --initialize-sourcing-demo
Remove-Item Env:SourcingDemo__Password
```

The command adds the three fixed evaluation employees (Tariq Salameh, Rana
Abuzaid, and Yousef Qasem) and approved demo suppliers
`supplier1@example.test`, `supplier2@example.test`, and
`supplier3@example.test`. It can be rerun without duplicating the accounts
or resetting existing passwords. When a sourcing officer creates a tender,
the three active employees must hold the SourcingManager, TechnicalCommittee, and FinancialCommittee roles respectively (one active employee per role), and the
request author fills the fourth seat.

### Clarifications and notifications

Apply the AddClarificationResolvedAt migration on an existing database.
The demo SQL script includes the same schema change.

Start a clarification from the history link on a purchase request, tender,
or submitted bid. Only the two participants can read the thread. The recipient
can answer while their communication route remains eligible; the sender can
then resolve it. Past threads remain available to their participants.

For a purchase request, the requester is matched to the recorded reviewer.
If a submitted request does not have a reviewer, the route is available only
when there is exactly one active sourcing officer. Tender and bid routes use
the tender creator, its assigned committee seats, and the bid owner. Top
Committee uses the existing shared final-approval queue.

Notifications are sent inside the site to the recipient of a question or
response. The unread count updates while the workspace is open.

### QR codes, protected PDFs and decision signatures

Open QR code and share link on a record's details page. The QR is generated
locally and preserves the destination's normal authentication and permissions.
Use the deployed site URL when sharing with other devices.

Bid attachments and evaluation report PDFs are stamped on each page with
the viewing account and access time. Set DocumentSecurity__WatermarkLabel to
customize the heading. Windows uses Arial; on other hosts set
DocumentSecurity__FontPath to an installed TrueType font.

Set DocumentSecurity__CertificatePath to the organization's PKCS#12 (.pfx)
certificate with its private key, and DocumentSecurity__CertificatePassword
via an environment secret or user secrets. Report copies and recorded tender
decision PDFs use this organization certificate; it does not represent a
personal certificate issued to the approver. Decision approval requires
explicit confirmation and the approver's current password. The personalized
watermark is applied before signing the copy. Source PDFs are unchanged.

### Tender lifecycle

Submit an approved requisition's tender draft to publish it. The background
scheduler opens and closes published tenders using the stored UTC dates,
checking every 15 seconds while the app is running. An officer can close an
open tender early through Compliance.

Round one requires three submitted, non-withdrawn offers. An officer may
reopen an insufficient first round once, retaining offers and inviting
suppliers without a submitted offer. The second round requires at least two
offers. Complete compliance before assigning the four evaluation members.

There must be exactly four active TopCommittee employee accounts when the
manager forwards a report. These four accounts are recorded on the tender.
An award requires all four approvals; any rejection cancels the tender.
The final outcome sends an in-app notification to participating suppliers.
