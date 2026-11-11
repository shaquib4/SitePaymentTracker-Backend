# KEPaymentTracker — Comprehensive System & Agent Handover Document

> **Document Version:** 3.7  
> **Last Updated:** November 2026  
> **Company / Tenant:** **Khan Enterprises**  
> **Codebase Paths:**  
> - **Frontend (React 18 + TS + Vite + PWA):** `D:\PaymentTracker-Frontend`  
> - **Backend (ASP.NET Core 10 Web API):** `D:\paymenttracker-backend`  
> - **Live Render Cloud Backend:** `https://sitepaymenttracker-backend.onrender.com`  
> - **Live Vercel Frontend:** `https://site-payment-tracker-frontend.vercel.app`  
> - **Live Cloud Database:** Supabase PostgreSQL 17 (`aws-0-ap-southeast-2.pooler.supabase.com:5432`, DB: `postgres`)  
> **Target Audience:** Incoming AI Agents, Software Engineers, DevOps Engineers  

---

## 1. Executive Overview & System Purpose

**KEPaymentTracker** is an enterprise-grade construction payment, daily wage, site expense tracking, and worker self-service system built specifically for **Khan Enterprises**. It solves operational challenges in multi-site civil construction, daily labourer muster tracking, wage audit controls, and office employee expense claims.

### Key Business Problems Solved
1. **Field Wage Exploitation Prevention**: Registered baseline daily wage rates are tracked and compared against payout requests. Discrepancies trigger automated audit warnings to prevent underpayment or wage diversion.
2. **Multi-Admin Governance**: Designed for executive oversight. Admin-initiated expense or wage requests require approval by a peer Admin, eliminating unilateral payouts.
3. **Accountant Period Boundaries**: Accountants have read-only gatekeeper access restricted by default to a 1-year window, but can formally submit a **Period Access Request** to Admins for historical or future audits.
4. **Site Financial Health & Measurement Books (MB)**: Measurement Books record verified field milestones. Sites track contract value vs. actual expenses. Site Profit/Loss calculations are withheld until the Admin marks final client payment or final MB received.
5. **Real-Time Strict Expense Separation**: Unapproved ("Pending") payment requests are strictly quarantined and never pollute expense pie charts or financial summaries until formally approved.
6. **Multi-Identifier Field Login & Optional Email**: Supervisors and site staff can log in using either their email address or a unique **6-digit User ID** (e.g. `521402`, `616244`, `400004`). Email is strictly optional for all non-admin employees.
7. **Dedicated Office Worker Role**: Office workers can independently submit claims (travel, supplies, utilities) and track personal historical payments with multi-year period filters, while being strictly isolated from company-wide financials.
8. **Direct Site View Approvals**: Admins can inspect and approve payment claims directly inside the dedicated Project Site view without switching to the master ledger.
9. **High-Performance Pagination & Shimmer UI (v3.4)**: Limits display to 20 records per page by default across all dashboards to eliminate DOM lag on annual filters, with animated shimmer loading placeholders and calendar quarter filters.
10. **True Backend Server-Side Pagination & SQL Summarization (v3.5)**: Eliminates unbounded full-table transfers across all primary endpoints (`GET /api/payments`, `/api/sites/{id}/expenses`, `/api/users/{id}/history`, `/api/users`, `/api/labourers`). PostgreSQL executes `CountAsync`, `GroupBy` financial KPIs, and dynamic pie-chart breakdowns across all matching rows while serializing only the active page (`pageSize = 20` default).
11. **Role-Aware Optional Descriptions & Requested/Approved Amount Editing (v3.6)**: `Description` is optional for all non-admin roles and required for Admin-initiated payment requests; during Admin approval, Admin can edit the employee's `Description` and adjust `ApprovedAmount` strictly `<= RequestedAmount` (and `> 0`), while `RequestedAmount` and `ApprovedAmount` are tracked and displayed across ledgers and Excel exports.
12. **Admin-Only Background Audit Pack — Breakdown PDF + Full Expenses Excel + Email + Audit Files Section (v3.7)**: Strictly restricted to `Admin`. Triggered directly beside the Expense Category Distribution breakdown header (in `AdminDashboard` and `SiteExpensesModal`), queuing a background worker (`ExportBackgroundWorker` + `AuditReportJobProcessor`) that generates a formal **Breakdown-only PDF** (`PdfBreakdownExportService` via QuestPDF) alongside a filtered **Expenses Excel** (`ExcelExportService` via ClosedXML) for all matching records in the selected period/filters, persists both binaries in PostgreSQL `"AuditReportJobs"`, dispatches email notification (`IEmailService` supporting Resend HTTP API, SMTP, or structured simulation fallback), and surfaces real-time downloads inside the Admin **Audit Files** section.

---

## 2. Cloud Architecture & Deployment Status

```
                           ┌────────────────────────────────────────┐
                           │         React 18 + Vite Frontend       │
                           │   Local: http://localhost:5173         │
                           │   Live: site-payment-tracker-frontend  │
                           │         .vercel.app                    │
                           └───────────────────┬────────────────────┘
                                               │ HTTPS / REST
                                               ▼
                           ┌────────────────────────────────────────┐
                           │      Render Docker Web Service         │
                           │  https://sitepaymenttracker-backend.   │
                           │             onrender.com               │
                           │   ASP.NET Core 10 (Alpine Container)   │
                           └───────────────────┬────────────────────┘
                                               │ IPv4 TLS (Session Pooler)
                                               ▼
                           ┌────────────────────────────────────────┐
                           │      Supabase PostgreSQL 17            │
                           │  aws-0-ap-southeast-2.pooler.          │
                           │           supabase.com:5432            │
                           └────────────────────────────────────────┘
```

### Live Service Endpoints
- **Render Backend Base URL**: `https://sitepaymenttracker-backend.onrender.com`
- **Health Check**: `https://sitepaymenttracker-backend.onrender.com/health` (Status `Healthy`)
- **API Swagger/OpenAPI**: `https://sitepaymenttracker-backend.onrender.com/swagger`
- **Auth Endpoint**: `https://sitepaymenttracker-backend.onrender.com/api/auth/login`
- **Payments Endpoint**: `https://sitepaymenttracker-backend.onrender.com/api/payments?year=2026`
- **Vercel Live Frontend**: `https://site-payment-tracker-frontend.vercel.app`

---

## 3. User Roles, Permissions & Demo Credentials

All test accounts use the default password: **`Password123!`**

| Role | Demo User Name | Login Identifier | 6-Digit ID | Core Permissions & Scopes |
| :--- | :--- | :--- | :--- | :--- |
| **Admin** | Shaquib Khan | `khanshaquib21@gmail.com` | `749201` | Full administrative control: approve/reject claims directly in master ledger or site view, settle project sites, upload MBs, generate new 6-digit employee accounts, review Accountant period requests. |
| **Accountant** | Ramesh Sharma | `521402` *(no email)* | `521402` | Read-only financial gatekeeper: verify vouchers, request clarifications, initiate own wage claims, export Excel ledgers, request access for historical years. |
| **Supervisor** | Surendra Kumar | `616244` *(no email)* | `616244` | Site operations: onboard & manage labourers, submit weekly wage claims against daily rates, submit site material expenses, view site muster roll. |
| **Office Worker** | Neha Sharma | `400004` / `officeworker@khanenterprises.com` | `400004` | **Self-service expense claims**: submit personal claims (travel, stationery, IT), track real-time approval status, audit personal historical payments with calendar period filters. Strictly isolated to own claims. |
| **Vendor** | Apex Supplies | `918234` *(no email)* | `918234` | Vendor portal placeholder (Phase 2 expansion). |

---

## 4. Key Functional Modules & Architectural Enhancements

### 4.1. Office Worker Self-Service Module
- **Backend Isolation (`PaymentService.cs`)**:
  - Automatically restricts queries for `OfficeWorker` to `userFilter = currentUserId`.
  - Non-restricted calendar year access: Office workers can audit multi-year historical claims (e.g. 2026, 2025, 2024) without requiring Admin period unlock.
- **Frontend Dashboard (`OfficeWorkerDashboard.tsx`)**:
  - Prominent `+ Request Payment / Claim` button connected to `InitiatePaymentModal`.
  - Historical Year selector (`Year 2026`, `Year 2025`, `Year 2024`...).
  - Period filter presets: `Full Year`, `Last 30 Days`, custom date pickers (`min-h-[42px]`).
  - Personal Summary Cards: Period Total Claims, Approved Claims, Pending Review, Lifetime Total Approved.
  - Category Spend Pie Chart (`ExpensePieChart`) for personal claims.
  - Touch-friendly mobile card view and desktop table view with admin approval notes.

### 4.2. Redesigned Site Expenses View & In-Site Approval Workflow
- **Modernized `SiteExpensesModal.tsx` / `SiteExpensesView`**:
  - Replaced cramped modal tables with a layout matching the Master Payment Ledger.
  - **Full-Screen Adaptable View**: In `AdminDashboard.tsx` and `AccountantDashboard.tsx`, selecting a site opens a full-page view with a top `← Back to Sites & Profitability` button.
  - **Top Financial Health Cards**: Final Payment Received, Total Site Expenses, Current Margin / Net Profit (₹ and %), MB Valuation.
  - **Calendar Period Filter Bar**: `All Site Records`, `Last 30 Days`, `Reset Filters`, responsive start/end date inputs (`min-h-[42px]`), Category and Status dropdowns, and search input.
  - **Site Expense Distribution Pie Chart (`ExpensePieChart`)**: Interactive breakdown of approved site spending by material, wages, equipment rental, and transport.
  - **Mobile Card Grid (`md:hidden`)**: Touch-friendly cards showing bold amounts, category/worker tags, calendar duration, click-to-call phone links for supervisors, and admin approval notes.
  - **Desktop Table View (`hidden md:block`)**: Spacious table with hover effects.
- **In-Site Admin Approval & Clarification Engine**:
  - **Approve & Categorize Action**: Directly from the site view, Admins can review pending claims, re-categorize into existing categories, or dynamically type and save new categories.
  - **Request Info / Clarification**: Admins can flag pending claims as `InfoRequested` and add specific audit questions for the supervisor.
  - **Multi-Admin Peer Approval Rule**: Strictly enforced (`item.userId === user?.userId`). Self-submitted claims show a disabled `Peer Approval Req (Self)` badge with `ShieldCheck` icon.
  - **Optimistic Concurrency & Data Integrity**: Backend `SiteExpenseItemDto` returns `version`, `categoryId`, and `infoRequestDetails`. Upon approval or clarification, queries (`site-expenses`, `sites-profitability`, `payments`, `dashboard-stats`) automatically invalidate to reflect fresh balances.

### 4.3. PWA Configuration & Mobile Installation
- **`vite-plugin-pwa` Implementation**:
  - Auto-updates enabled with `registerType: 'autoUpdate'`.
  - Workbox configured with `cleanupOutdatedCaches: true`, `clientsClaim: true`, `skipWaiting: true`, and `NetworkFirst` HTML navigation.
  - Self-healing script in `index.html` catches missing asset errors, purges legacy caches, and safely recovers without reload loops.
- **Apple iOS PWA Support**:
  - Full meta tags: `apple-mobile-web-app-capable: yes`, `apple-mobile-web-app-status-bar-style: black-translucent`, `apple-mobile-web-app-title: KE Tracker`.
  - Manifest declared with `purpose: 'any'` and `purpose: 'maskable'` for 192px and 512px icons.
- **In-App Install Prompt & Guidance**:
  - `[ 📲 Install App ]` button in `Navbar.tsx` and top mobile banner.

### 4.4. Site Expenses & Profitability View Stability Fix (v3.3)
- **Resolved React Rule of Hooks Invariant Crash**:
  - In `AdminDashboard.tsx`, conditional early return statements (`if (viewingSiteProfitability)` and `if (viewingEmployeeId)`) were previously placed prior to `pieCategoryBreakdowns = useMemo(...)`.
  - When an Admin clicked "View Expenses" on any site card, the component skipped subsequent hooks on re-render, causing React to throw `Rendered fewer hooks than expected`.
  - Both conditional full-page view returns were relocated to strictly follow all hook definitions and memoized calculations.
- **Defensive Null-Safety & Fallback Resilience in `SiteExpensesModal.tsx`**:
  - Normalized all `siteProfitability` attributes with safe number casting (`Number(val ?? 0)`) to guard against unpopulated or null fields.
  - Formatted currency values with `(Number(item.amount) || 0).toLocaleString('en-IN')` and `.toFixed(1)`.
  - Added visual error boundaries and `isError` retry buttons to the mobile card grid and desktop table view.
  - Guidance modal detects iOS and Google App in-app webview: provides clear instructions to open in Safari and tap **"Add to Home Screen"** (`[+]`).

### 4.5. High-Performance Pagination & Shimmer UI Engine (v3.4)
- **Modular Reusable Pagination Component (`Pagination.tsx`)**:
  - Implements a standard limit of **20 records per page (default)** across Master Payment Ledger, Labourers Roster, Employees Management, Dedicated Site Expenses Ledger, Dedicated Employee Expenses Page, Accountant Ledger, and Office Worker Dashboard.
  - Page size selector (`10`, `20`, `50`, `100` records per page).
  - Compact, touch-friendly UI showing `Showing X to Y of Z records`, First, Prev, windowed page numbers (`1 ... 4 [5] 6 ... 10`), Next, and Last buttons.
  - Dynamic color theme accents: `'amber'` (Admin), `'emerald'` (Accountant), `'blue'` (Office Worker / Site).
  - Automatic `useEffect` state reset to page 1 whenever search, date ranges, categories, or status filters change.
- **Strict Separation of Render Pagination vs Summary Totals**:
  - Financial metric cards (Grand Totals, Approved, Pending Queue, Info Requested) and Pie Chart category breakdowns calculate across the **entire filtered dataset** (e.g. all 365 days of the year), ensuring 100% financial audit accuracy while rendering only the sliced 20 items to DOM.
- **Dark-Mode Wave Shimmer Skeletons (`ShimmerLoader.tsx` & `src/index.css`)**:
  - Replaces plain-text loading spinners with animated wave skeleton components (`.shimmer-wave` gradient sweep).
  - `TableShimmer`: Pulsing multi-column skeleton rows matching exact desktop table column geometries and action buttons.
  - `CardShimmer`: Pulsing skeleton cards for mobile viewports showing headers, badge chips, amounts, and duration bars.
- **Calendar Quarter & Date Presets (`QuarterFilterBar.tsx` & `quarterUtils.ts`)**:
  - Provides quick-selector pills for `Full Year`, `Q1: Jan-Mar`, `Q2: Apr-Jun`, `Q3: Jul-Sep`, `Q4: Oct-Dec`, `Last Quarter`, `Last 30 Days`, and `Reset`.
  - Smart Quarter calculation with year rollover: When in Q1, clicking "Last Quarter" automatically calculates Q4 of the previous year.
  - Visual active state pill indication and verification sub-bar (`Active Range: YYYY-MM-DD → YYYY-MM-DD`).

### 4.6. True Backend Server-Side Pagination & SQL Summarization Engine (v3.5)
- **PostgreSQL-First Filtering, Aggregates & Slicing**:
  - `GET /api/payments` (`PaymentRepository.GetPagedPaymentsAsync`) performs all filtering (`Year`, `Status`, `SiteId`, `StartDate`, `EndDate`, `Search`, `Scope`, `OnlyMine`, `Category`) in SQL, computes `PaymentPagedSummaryDto` (`GrandTotalAmount`, `TotalApprovedAmount`, `TotalCompletedAmount`, `TotalPendingAmount`, `TotalInfoRequestedAmount`, `TotalRejectedAmount`, counts, and `CategoryBreakdowns`) across the full matching dataset using `GroupBy`, and transfers only `.Skip((page - 1) * pageSize).Take(pageSize)` rows (`pageSize = 20` default, max `200`).
  - Dynamic pie-chart category breakdown is computed on the server: reflects the active `Status` filter when selected, or defaults to `Approved + Completed` (falling back to all if none) while ignoring `Category` so pie slices remain interactive.
  - `GET /api/sites/{siteId}/expenses` returns `PagedSiteExpensesResponseDto` with SQL `status`, `search`, date, and `category` filtering, distinct site categories, KPI sums, and `CategoryBreakdowns`.
  - `GET /api/users/{id}/history` returns extended `UserHistoryResponse` with `siteId`, `status`, `search`, `page`, `pageSize` filtering, `FilteredCount`, `TotalRequestsCount`, and `CategoryBreakdowns`.
  - `GET /api/users` and `GET /api/labourers` return `PagedUsersResponseDto` and `PagedLabourersResponseDto` respectively, with `?all=true` / `?role=Supervisor&all=true` lightweight lookup support strictly for form/filter dropdowns.
- **Debounced Search & Zero Full-Table Transfers on Frontend**:
  - Added shared `useDebouncedValue` hook (`300ms`) across `AdminDashboard`, `AccountantDashboard`, `OfficeWorkerDashboard`, `SiteExpensesModal`, and `EmployeeExpensesView`.
  - `SupervisorDashboard` passes `scope` (`wage_requests`, `my_wages`, `material_requests`) plus `page`/`pageSize` and uses `pageSize=1` summary queries for KPIs/pie charts.

### 4.7. Mobile Layout & Date Picker Sizing
- Date pickers styled across all dashboards with `min-height: 42px !important; height: 42px !important; [color-scheme: dark]` to match button and dropdown heights on iOS Safari and Chrome.
- Top tab headers use `.horizontal-scroll-container` with `whitespace-nowrap flex-shrink-0` for smooth swipeable navigation.

### 4.8. Admin-Only Background Audit Pack & Audit Files Section (v3.7)
- **Endpoints (`ReportsController.cs`)**:
  - `POST /api/reports/admin-audit-pack` (`[Authorize(Roles = "Admin")]`): Captures active `Year`, `StartDate`, `EndDate`, `Status`, `Category`, `SiteId`, `Search`, and optional `RecipientEmail`; creates an `AuditReportJob` (`Queued`) in PostgreSQL and enqueues work onto `Channel<ExportJobRequest>`.
  - `GET /api/reports/admin-files` (`[Authorize(Roles = "Admin")]`): Projects metadata for the tenant's recent audit packs (without loading `bytea` file blobs).
  - `GET /api/reports/admin-files/{jobId}/download/{format}` (`pdf` | `xlsx`): Authenticated `[Authorize(Roles = "Admin")]` JWT bearer download OR constant-time per-job `DownloadToken` query parameter verification for clickable email links.
  - `DELETE /api/reports/admin-files/{jobId}` (`[Authorize(Roles = "Admin")]`): Removes old audit pack records.
- **Breakdown-Only Audit PDF (`PdfBreakdownExportService.cs`)**: Uses `QuestPDF` (`LicenseType.Community`) to generate an A4 audit paper containing Period & Active Filter Scope, Executive Financial KPIs, Category Breakdown schedule with percentage allocation bars matching the active status scope, Workflow Status Summary, companion Excel file reference, and Auditor Sign-Off block.
- **Companion Filtered Expenses Excel (`ExcelExportService.cs`)**: Uses `ClosedXML` to export all itemized expenses matching the active period & filters (including `Site / Project`, `Description`, `Requested Amount`, `Approved Amount`, and `SUM` formulas).
- **Email Notification Service (`EmailService.cs`)**: Supports Resend API (`Email:ResendApiKey`), SMTP (`Email:SmtpHost` / `SmtpPort` / `SmtpUser` / `SmtpPassword`), and graceful simulation logging fallback stored in `EmailDeliveryNote`.
- **Frontend Audit UI (`AuditFilesPanel.tsx`, `useAdminAuditReports.ts`)**: Strictly gated to `user?.role === 'Admin'`, places a **`Breakdown PDF + Excel`** action button right beside the Breakdown chart header in `AdminDashboard` and `SiteExpensesModal`, auto-polls running jobs every `2.5s`, displays email delivery status badges, and provides direct 1-click PDF and Excel downloads inside the Admin **Audit Files** tab and drawer modal.

---

## 5. Repository Structure & Key Files

### Backend (`D:\paymenttracker-backend`)
```
src/
├── EnterprisePaymentSystem.Api/
│   ├── Controllers/
│   │   ├── AuthController.cs          # Login with Email or 6-digit User ID
│   │   ├── PaymentsController.cs      # Expenses, Wage requests, Approval engine
│   │   ├── SitesController.cs         # Sites, Profitability, Site expenses with Version/Category
│   │   ├── UsersController.cs         # User generation, User lifetime history
│   │   ├── ReportsController.cs       # Admin-only Audit Pack queue, Audit Files list & PDF/Excel download
│   │   └── PeriodAccessRequestsController.cs # Accountant period requests
│   └── Program.cs                     # JWT, DI, Supabase / PostgreSQL config
├── EnterprisePaymentSystem.Application/
│   ├── DTOs/                          # PaymentDtos, AuthDtos, SiteDtos, ReportDtos
│   ├── Services/
│   │   └── PaymentService.cs          # Strict role isolation & period validation
│   └── Validators/                    # FluentValidation rules
├── EnterprisePaymentSystem.Domain/
│   ├── Entities/                      # PaymentRequest, Labourer, Site, User, AuditReportJob
│   └── Enums/                         # UserRole (OfficeWorker = 4), PaymentStatus, AuditReportStatus
└── EnterprisePaymentSystem.Infrastructure/
    ├── Data/
    │   ├── ApplicationDbContext.cs
    │   └── DbInitializer.cs           # Seed data + idempotent Postgres schema upgrades
    ├── BackgroundJobs/
    │   └── ExportBackgroundWorker.cs  # Asynchronous Channel consumer for Audit Packs & Excel reports
    ├── Services/
    │   ├── AuditReportJobProcessor.cs # Generates PDF+Excel, saves DB bytes & triggers email
    │   ├── PdfBreakdownExportService.cs # QuestPDF executive Breakdown PDF generator
    │   ├── ExcelExportService.cs      # ClosedXML itemized ledger export
    │   └── EmailService.cs            # Resend / SMTP / fallback audit notification mail
    └── Repositories/
```

### Frontend (`D:\PaymentTracker-Frontend`)
```
src/
├── components/
│   ├── Navbar.tsx                     # Branding, Cyan Office Worker badge, Install PWA trigger
│   ├── LoginPage.tsx                  # Email / 6-digit ID login + quick-role switcher
│   ├── Pagination.tsx                 # Modular pagination component (default 20 records/page)
│   ├── QuarterFilterBar.tsx           # Calendar quarter selector pills (Full Year, Q1-Q4, Last Quarter)
│   ├── ShimmerLoader.tsx              # Dark-mode animated skeleton rows and mobile cards
│   ├── SiteExpensesModal.tsx          # Dedicated Site Expense Ledger + Admin Approval & Audit Pack trigger
│   ├── AuditFilesPanel.tsx            # Admin-only Audit Files tab panel & modal drawer (PDF + Excel downloads)
│   ├── ExpensePieChart.tsx            # Spend category distribution chart
│   ├── InstallPwaModal.tsx            # Safari / Chrome home-screen install instructions
│   └── InitiatePaymentModal.tsx       # Expense claim modal with dynamic category selector
├── core/
│   ├── hooks/useAdminAuditReports.ts  # Admin Audit Pack queue, polling & authenticated blob download hook
│   ├── types/index.ts                 # TS models (UserRole, SiteExpenseItemDto, AuditReportJobItem)
│   ├── utils/quarterUtils.ts          # Smart quarter ranges & rollover logic
│   └── auth/AuthContext.tsx           # Authentication state & role routing
└── modules/
    ├── admin/AdminDashboard.tsx       # Master ledger, Sites & Profitability, Audit Files, User generator
    ├── accountant/AccountantDashboard.tsx # Read-only financial ledger, Period access modal
    ├── supervisor/SupervisorDashboard.tsx # Labourer muster roll & wage claims
    └── officeworker/OfficeWorkerDashboard.tsx # Office worker claim history & submitter
```

---

## 6. How to Run Locally

### Start Backend API
```powershell
cd D:\paymenttracker-backend
$env:DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet run --project "src\EnterprisePaymentSystem.Api\EnterprisePaymentSystem.Api.csproj"
# API will listen on http://localhost:5000 (Swagger: http://localhost:5000/swagger)
```

### Start Frontend Dev Server
```powershell
cd D:\PaymentTracker-Frontend
npm run dev -- --host 0.0.0.0 --port 5173
# Frontend available on http://localhost:5173
```

---

## 7. Crucial Guidelines & Development Rules for Future Agents

1. **Strictly Local Changes**: When requested not to push to remote (`git push origin main`), leave all changes local on the active `main` branch.
2. **Tenant Branding Integrity**: The organization is strictly **Khan Enterprises**. Never introduce legacy SaaS branding or "Flat 30k" tags.
3. **Multi-Admin Peer Approval Rule**: An Admin can NEVER approve their own payment request (`payment.userId === currentUserId`). Display disabled badge `Peer Approval Req (Self)`.
4. **Optimistic Concurrency**: Whenever approving or requesting clarification on payment requests, always pass the current `version` token in the request payload.
5. **Pending Claim Isolation**: Pending claims must remain isolated and never leak into Approved Spend totals or Pie Chart breakdowns.
