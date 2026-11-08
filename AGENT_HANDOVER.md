# KEPaymentTracker — Comprehensive System & Agent Handover Document

> **Document Version:** 3.1  
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
  - Guidance modal detects iOS and Google App in-app webview: provides clear instructions to open in Safari and tap **"Add to Home Screen"** (`[+]`).

### 4.4. Mobile Layout & Date Picker Sizing
- Date pickers styled across all dashboards with `min-height: 42px !important; height: 42px !important; [color-scheme: dark]` to match button and dropdown heights on iOS Safari and Chrome.
- Top tab headers use `.horizontal-scroll-container` with `whitespace-nowrap flex-shrink-0` for smooth swipeable navigation.

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
│   │   └── PeriodAccessRequestsController.cs # Accountant period requests
│   └── Program.cs                     # JWT, DI, Supabase / PostgreSQL config
├── EnterprisePaymentSystem.Application/
│   ├── DTOs/                          # PaymentDtos, AuthDtos, SiteDtos
│   ├── Services/
│   │   └── PaymentService.cs          # Strict role isolation & period validation
│   └── Validators/                    # FluentValidation rules
├── EnterprisePaymentSystem.Domain/
│   ├── Entities/                      # PaymentRequest, Labourer, Site, User
│   └── Enums/                         # UserRole (OfficeWorker = 4), PaymentStatus
└── EnterprisePaymentSystem.Infrastructure/
    ├── Data/
    │   ├── ApplicationDbContext.cs
    │   └── DbInitializer.cs           # Seed data with Office Worker Neha Sharma (400004)
    └── Repositories/
```

### Frontend (`D:\PaymentTracker-Frontend`)
```
src/
├── components/
│   ├── Navbar.tsx                     # Branding, Cyan Office Worker badge, Install PWA trigger
│   ├── LoginPage.tsx                  # Email / 6-digit ID login + quick-role switcher
│   ├── SiteExpensesModal.tsx          # Dedicated Site Expense Ledger + Admin Approval Workflow
│   ├── ExpensePieChart.tsx            # Spend category distribution chart
│   ├── InstallPwaModal.tsx            # Safari / Chrome home-screen install instructions
│   └── InitiatePaymentModal.tsx       # Expense claim modal with dynamic category selector
├── core/
│   ├── types/index.ts                 # TS models (UserRole, SiteExpenseItemDto with version)
│   └── auth/AuthContext.tsx           # Authentication state & role routing
└── modules/
    ├── admin/AdminDashboard.tsx       # Master ledger, Sites & Profitability, User generator
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
