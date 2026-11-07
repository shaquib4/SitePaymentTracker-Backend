# KEPaymentTracker — Comprehensive System & Agent Handover Document

> **Document Version:** 1.1  
> **Generated:** November 2026  
> **Project Folders:**  
> - **Frontend:** `D:\PaymentTracker-Frontend`  
> - **Backend:** `D:\paymenttracker-backend`  
> - **Legacy Backup Root:** `D:\KEPaymentTracker`  
> **Target Audience:** Incoming AI Agents, Software Engineers, and DevOps Engineers  

---

## 1. Executive Overview & System Purpose

**KEPaymentTracker** is an enterprise-grade construction payment, daily wage, and site expense tracking web application. Built for real-world construction infrastructure companies (e.g., *BuildCon Infra Ltd*), it addresses the operational challenges of managing multi-site field finances, labourer attendance, wage payouts, and expense compliance.

### Key Business Problems Solved
1. **Field Wage Exploitation Prevention**: Supervisors in the field can register labourers, but registered baseline daily wage rates are tracked and compared against payout requests. Discrepancies trigger automated audit warnings to prevent underpayment or wage diversion.
2. **Multi-Admin Governance**: Multi-admin capability (designed for 4 admins). Admin-initiated expense or wage requests require approval by another Admin, eliminating single-point unilateral payouts.
3. **Accountant Period Boundaries**: Accountants have read-only access restricted by default to a 1-year window, but can formally submit a **Period Access Request** to Admins for historical or future audits. Approval dynamically expands their visibility.
4. **Site Financial Health & Measurement Books (MB)**: Measurement Books record verified field construction milestones. Sites track contract value vs actual expenses. Site Profit/Loss calculations are withheld until the Admin marks the final client payment / final MB received.
5. **Real-Time Strict Expense Separation**: Unapproved ("Pending") payment requests are strictly quarantined and never pollute expense pie charts or financial summaries until formally approved.
6. **Multi-Identifier Field Login**: Construction supervisors and staff can log in using either their standard email address or a unique **6-digit User ID** (e.g. `749201`).

---

## 2. Architecture & Technology Stack

```
                          ┌───────────────────────────┐
                          │    Browser / Mobile PWA   │
                          │   (React 18 + Vite + TS)  │
                          └─────────────┬─────────────┘
                                        │ HTTP / JSON (Axios)
                                        ▼
                          ┌───────────────────────────┐
                          │   ASP.NET Core 10 Web API │
                          │     (RESTful + JWT Auth)  │
                          └──────┬────────────────┬───┘
                                 │                │
            ┌────────────────────┘                └────────────────────┐
            ▼                                                          ▼
┌───────────────────────────┐                              ┌───────────────────────────┐
│     Entity Framework      │                              │  Background Worker Queue  │
│     Core (Npgsql Driver)  │                              │   (Async Excel / Reports) │
└───────────┬───────────────┘                              └───────────────────────────┘
            ▼
┌───────────────────────────┐
│     PostgreSQL 17         │
│  (DB: `kepayment_db`)     │
└───────────────────────────┘
```

### Technology Matrix
- **Backend**:
  - **Framework**: .NET 10 (C#) ASP.NET Core Web API
  - **Architecture**: Clean Architecture / Domain-Driven Design (Domain, Application, Infrastructure, Api, UnitTests)
  - **ORM**: Entity Framework Core 9.x / 10.x with `Npgsql.EntityFrameworkCore.PostgreSQL`
  - **Security**: JWT Bearer Tokens (`Microsoft.AspNetCore.Authentication.JwtBearer`), BCrypt.Net password hashing
  - **Background Processing**: `IBackgroundExportQueue` + `ExportBackgroundWorker` (HostedService) for offloading heavy Excel/CSV generation
- **Frontend**:
  - **Framework**: React 18, TypeScript, Vite
  - **Styling**: Tailwind CSS, Mobile-first responsive grid layouts
  - **Charts & UI**: Recharts (`PieChart`, `Pie`, `Cell`, `Tooltip`, `Legend`), Lucide React icons
  - **State/Auth**: React Context API (`AuthContext`), LocalStorage token persistence
- **Database**:
  - **Engine**: PostgreSQL 17 on `localhost:5432`
  - **Database Name**: `kepayment_db`
  - **Credentials**: Username `postgres`, Password `Moha@16778`

---

## 3. Database Schema & Data Models

All tables are in the `public` schema of `kepayment_db`.

### 3.1 `Users`
Tracks all enterprise users across four distinct roles.
- `Id` (UUID, PK)
- `TenantId` (UUID, FK -> Tenants)
- `FullName` (VARCHAR)
- `Email` (VARCHAR, Unique per tenant)
- `SixDigitUserId` (VARCHAR(6), Unique per tenant)
- `PasswordHash` (VARCHAR, BCrypt)
- `Role` (INT: `0 = Admin`, `1 = Accountant`, `2 = Supervisor`, `3 = Vendor`)
- `Department` (VARCHAR)
- `PhoneNumber` (VARCHAR)
- `IsActive` (BOOLEAN) — Controlled via Admin toggle; inactive users cannot log in.
- `CreatedAtUtc` (TIMESTAMPTZ)

### 3.2 `Labourers`
Field workforce assigned to construction sites and supervisors.
- `Id` (UUID, PK)
- `TenantId` (UUID)
- `FullName` (VARCHAR)
- `PhoneNumber` (VARCHAR)
- `AadhaarNumber` (VARCHAR(12))
- `DailyWageRate` (DECIMAL) — Baseline registered daily wage.
- `AssignedSupervisorId` (UUID, FK -> Users) — Supervisor managing this labourer.
- `SiteId` (UUID, FK -> Sites) — Current site assignment.
- `CreatedByUserId` (UUID, FK -> Users) — Audit trail of who registered the labourer.
- `IsActive` (BOOLEAN) — Soft delete/deactivation flag preserving historical financial records.
- `CreatedAtUtc` (TIMESTAMPTZ)

### 3.3 `Sites`
Construction project locations.
- `Id` (UUID, PK)
- `TenantId` (UUID)
- `Name` (VARCHAR)
- `Location` (VARCHAR)
- `ContractValue` (DECIMAL, Nullable)
- `FinalPaymentReceived` (DECIMAL, Nullable)
- `IsSettled` (BOOLEAN) — Indicates final MB / client settlement received.
- `SettledAtUtc` (TIMESTAMPTZ, Nullable)
- `IsActive` (BOOLEAN)
- `CreatedAtUtc` (TIMESTAMPTZ)

### 3.4 `PaymentRequests`
Core transactional table for all wage payouts and expenses.
- `Id` (UUID, PK)
- `TenantId` (UUID)
- `Type` (INT: `0 = LabourWage`, `1 = VendorMaterial`, `2 = EquipmentRental`, `3 = Fuel`, `4 = SupervisorReimbursement`, `5 = AccountantWage`, `6 = AdminWage`)
- `Amount` (DECIMAL)
- `CategoryId` (UUID, FK -> Categories)
- `SiteId` (UUID, FK -> Sites)
- `LabourerId` (UUID, FK -> Labourers, Nullable)
- `DaysWorked` (INT, Nullable)
- `DailyRateApplied` (DECIMAL, Nullable) — Rate used for this specific payout.
- `Status` (INT: `0 = Pending`, `1 = Approved`, `2 = Rejected`, `3 = Paid`)
- `RequestedByUserId` (UUID, FK -> Users)
- `ApprovedByUserId` (UUID, FK -> Users, Nullable)
- `RequestedAtUtc` (TIMESTAMPTZ)
- `ApprovedAtUtc` (TIMESTAMPTZ, Nullable)
- `PaymentDate` (DATE / TIMESTAMPTZ)
- `Description` / `Notes` (VARCHAR)
- `ReceiptUrl` (VARCHAR, Nullable)

### 3.5 `PaymentAuditLogs`
Immutable append-only audit trail for every status transition.
- `Id` (UUID, PK)
- `PaymentRequestId` (UUID, FK -> PaymentRequests)
- `Action` (VARCHAR: `Created`, `Approved`, `Rejected`, `Paid`, `Reassigned`)
- `PerformedByUserId` (UUID, FK -> Users)
- `TimestampUtc` (TIMESTAMPTZ)
- `Notes` (VARCHAR)

### 3.6 `MeasurementBooks` (MB)
Physical verification logs of completed site construction milestones.
- `Id` (UUID, PK)
- `TenantId` (UUID)
- `SiteId` (UUID, FK -> Sites)
- `EntryDate` (DATE)
- `WorkDescription` (TEXT)
- `VerifiedBy` (VARCHAR)
- `AttachmentUrl` (VARCHAR, Nullable) — Uploaded scan of physical MB.
- `TotalValue` (DECIMAL)
- `IsFinal` (BOOLEAN) — When true, triggers site settlement feasibility.
- `CreatedAtUtc` (TIMESTAMPTZ)

### 3.7 `PeriodAccessRequests`
Accountant time-window extension requests.
- `Id` (UUID, PK)
- `TenantId` (UUID)
- `RequestedByUserId` (UUID, FK -> Users)
- `TargetStartDate` (TIMESTAMPTZ)
- `TargetEndDate` (TIMESTAMPTZ)
- `Reason` (TEXT)
- `Status` (INT: `0 = Pending`, `1 = Approved`, `2 = Rejected`)
- `ApprovedByAdminId` (UUID, FK -> Users, Nullable)
- `CreatedAtUtc` (TIMESTAMPTZ)

---

## 4. Chronological Implementation History & Rationale

Here is the exact progression of user requests, problems encountered, and solutions engineered:

### Milestone 1: Role Permissions & Accountant Data Boundaries
- **User Request**: Clarify whether Admin can request information, not only the Accountant; clarify Accountant screens and read permissions.
- **Problem**: Previously, accountants had unrestricted access or unclear operational boundaries.
- **Solution & Rationale**:
  - Implemented `PeriodAccessRequestsController.cs` and `PeriodAccessRequest` domain model.
  - Set default accountant read boundary to the last 1 year.
  - Implemented the `PeriodAccessModal.tsx` in frontend: Accountants can select a custom start and end date and submit an access request to the Admin.
  - Admin dashboard displays incoming period access requests with one-click **Approve** or **Reject** actions.

### Milestone 2: Labour Management, Site Allocation & Anti-Exploitation
- **User Request**: Separate list of labourers; track who added whom; allow Admin to assign/reassign labourers to any supervisor or site; prevent supervisor wage exploitation.
- **Problem**: Lack of transparency in labour hiring led to risk of field supervisors registering fictitious labourers or taking cuts from daily wages.
- **Solution & Rationale**:
  - Added `LabourersController.cs` with full CRUD, filter by supervisor, and filter by site.
  - Recorded `CreatedByUserId` alongside `AssignedSupervisorId`.
  - Created `LabourerAuditModal.tsx`: Visual audit of labourer history showing registered daily wage rate (`DailyWageRate`) alongside every payment request's `DailyRateApplied`. Flagged discrepancies in yellow/red tags.
  - Created `EditLabourerModal.tsx`: Enables both Admins and Supervisors to reassign a labourer to another site or update rates.

### Milestone 3: Supervisor & Admin Deactivation vs Hard Deletion
- **User Request**: Supervisors should be able to deactivate labourers; Admin can deactivate or delete both labourers and users, but payment history must be maintained permanently.
- **Problem**: Cascading deletes in SQL would destroy audit history and violate accounting regulations.
- **Solution & Rationale**:
  - Implemented soft deactivation: setting `IsActive = false`.
  - In `PaymentsController.cs` and EF Core queries, historical `PaymentRequests` join against labourers regardless of active status (`IgnoreQueryFilters()` or explicit nullable joins), ensuring financial records and payment histories remain 100% intact even if a worker leaves.

### Milestone 4: Multi-Admin Payouts & Wage Requests for Staff
- **User Request**: Accountants and Supervisors should be able to request wages; Admin can initiate payment requests, and another Admin approves (multi-admin governance).
- **Problem**: Risk of unilateral payouts if an Admin can approve their own wage request.
- **Solution & Rationale**:
  - Expanded `PaymentType` to include `SupervisorReimbursement`, `AccountantWage`, and `AdminWage`.
  - Added multi-admin validation: `request.RequestedByUserId != currentUserId` required during approval of Admin wage requests.

### Milestone 5: Financial Charts, Calendar Filtering & MB (Measurement Book)
- **User Request**: Expense pie chart per site; calendar date filter (not just text range); drill-down to employee expenses; unapproved/pending payments MUST NOT appear in pie charts; Site P&L only calculated on final payment/final MB.
- **Problem**:
  - Pending expenses were inflating expense totals on charts before manager approval.
  - Fixed-interval date filtering lacked granularity for custom construction billing cycles.
- **Solution & Rationale**:
  - Created `ExpensePieChart.tsx` and `SitePieChart.tsx` using Recharts. Filtered datasets to `status === PaymentStatus.Approved || status === PaymentStatus.Paid`. All pending items are excluded.
  - Added calendar date range inputs (`startDate`, `endDate`) plus quick presets ("Last 30 Days", "Year-to-Date").
  - Created `EmployeeExpensesView.tsx`: Clicking an employee or supervisor navigates to their dedicated expense ledger with approval status, payment dates, and categories.
  - Added `MeasurementBook` entity and site settlement checks (`IsSettled`, `FinalPaymentReceived`). Profit & Loss remains marked as "In Progress" until final client payment is logged.

### Milestone 6: Mobile Responsiveness Overhaul
- **User Request**: Desktop is okay, but mobile view is cramped and unusable. Use grids and responsive layouts.
- **Solution & Rationale**:
  - Replaced cramped horizontal tables with responsive card grids (`grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4`).
  - Added sticky mobile headers, collapsible navigation bars, and touch-optimized buttons (`min-h-[44px]`).

### Milestone 7: Database Reset & Primary Admin Setup
- **User Request**: Clean slate: wipe test data, keep schema, establish primary admin `Shaquib Khan` (`khanshaquib21@gmail.com`), simplify filters to Last 30 Days and Year.
- **Solution & Rationale**:
  - Executed clean schema reset against PostgreSQL `kepayment_db`.
  - Seeded primary Admin:
    - Name: `Shaquib Khan`
    - Email: `khanshaquib21@gmail.com`
    - 6-Digit User ID: `749201`
    - Password: `Password123!` (BCrypt `$2a$11$ooRMTtAZRiOqLjebkXH9Du5zXm.nJzic3UDSHKWyW/0QxhQ.ceGfC`)
    - Tenant: `BuildCon Infra Ltd` (`11111111-1111-1111-1111-111111111111`)

### Milestone 8: Multi-Identifier Login Bug Fix & Supervisor Reactivation
- **User Request**: "Not able to login after creating supervisor user through newly created user credentials; give db details for DBeaver; create .gitignore files."
- **Root Causes Discovered**:
  1. The user was deactivated (`IsActive = false`) because the old `Deactivate` endpoint unconditionally set `IsActive = false`.
  2. The frontend login form had `<input type="email">`, which browser HTML5 validation rejected when users typed 6-digit User IDs like `616244`.
- **Solution & Rationale**:
  - Updated [LoginPage.tsx](file:///D:/KEPaymentTracker/frontend/src/components/LoginPage.tsx) to `<input type="text">` with placeholder `"name@company.com or 6-digit ID (e.g. 749201)"`.
  - Changed `Deactivate(Guid id)` in [UsersController.cs](file:///D:/KEPaymentTracker/backend/src/EnterprisePaymentSystem.Api/Controllers/UsersController.cs) to toggle `user.IsActive = !user.IsActive`.
  - Set all active users to `IsActive = true` in PostgreSQL.
  - Synchronized supervisor test passwords (`Suredra` `616244` and `Suresh` `349104`) to `Password123!`.
  - Provided exact DBeaver connection parameters.
  - Created `.gitignore` in root, backend, and frontend. Cleaned repository tracking.

---

## 5. Current System State & Verified Credentials

### Active Running Services
| Service | Address / Port | Background Task ID |
| :--- | :--- | :--- |
| **Frontend (Vite Dev Server)** | `http://localhost:5173` | `task-1091` |
| **Backend API (ASP.NET Core)** | `http://localhost:5000` | `task-2462` |
| **Database (PostgreSQL 17)** | `localhost:5432` (`kepayment_db`) | System Service |

### Seeded Users Available for Testing
All users are active and use the password: **`Password123!`**

| Full Name | Role | Email | 6-Digit ID | Password |
| :--- | :--- | :--- | :--- | :--- |
| **Shaquib Khan** | **Admin** | `khanshaquib21@gmail.com` | `749201` | `Password123!` |
| **Suredra** | **Supervisor** | `xyz@gmail.com` | `616244` | `Password123!` |
| **Suresh** | **Supervisor** | `xyyz@gmail.com` | `349104` | `Password123!` |

*Note: You can log into the web app using either the email or the 6-digit User ID in the first input box.*

---

## 6. How to Build, Run & Test

### 6.1 Backend (.NET 10)
```powershell
# Navigate to backend directory
cd D:\KEPaymentTracker\backend

# Build solution
dotnet build EnterprisePaymentSystem.slnx

# Run API Server (bound to http://localhost:5000)
dotnet run --project src\EnterprisePaymentSystem.Api\EnterprisePaymentSystem.Api.csproj
```

### 6.2 Frontend (React / Vite)
```powershell
# Navigate to frontend directory
cd D:\KEPaymentTracker\frontend

# Install dependencies (if fresh clone)
npm install

# Start Vite dev server (bound to http://localhost:5173)
npm run dev -- --host 0.0.0.0
```

### 6.3 Database Inspection via CLI or DBeaver
- **psql CLI**:
  ```powershell
  $env:PGPASSWORD='Moha@16778'
  & "D:\PG\Postgres\bin\psql.exe" -U postgres -d kepayment_db
  ```
- **DBeaver**:
  - Host: `localhost` | Port: `5432` | DB: `kepayment_db`
  - Username: `postgres` | Password: `Moha@16778`

---

## 7. Critical Agent Operational Guidelines & Gotchas

If you are an AI agent picking up this codebase, heed the following lessons learned:

1. **Windows File Locks on DLLs**:
   - Running `dotnet run` locks `EnterprisePaymentSystem.Api.dll` and referenced project assemblies.
   - **Never run `dotnet build` while the backend API task is running**, or you will encounter `error MSB3027: The process cannot access the file because it is being used by another process`.
   - Always stop/kill the running API background task before rebuilding, then restart the API daemon.
2. **PostgreSQL Network & Sandbox Permissions**:
   - The PostgreSQL server runs on port 5432 on the host. Commands invoking `psql.exe` or connecting to PostgreSQL must run with `BypassSandbox: true` to access TCP sockets.
   - The PostgreSQL binary is located at `D:\PG\Postgres\bin\psql.exe`.
3. **Database Case Sensitivity**:
   - In EF Core with PostgreSQL, table and column names generated by EF Core are double-quoted and case-sensitive (e.g. `"Users"`, `"Labourers"`, `"IsActive"`).
   - In raw SQL queries, always quote identifiers: `SELECT "Id", "FullName" FROM "Users";`.
4. **Login Request Payload**:
   - The backend `LoginRequest` DTO property is named `Email` (e.g. `{"email": "...", "password": "..."}`). Do not send `identifier` as the JSON property key; the backend `AuthService` handles matching the `Email` string against either the email, the 6-digit ID, or normalized name.
5. **Git Hygiene**:
   - Root `.gitignore` is fully configured. Avoid using `git add -A` blindly if temporary scripts (`.sql` dumps or scratch files) are created. Always inspect `git status` first.

