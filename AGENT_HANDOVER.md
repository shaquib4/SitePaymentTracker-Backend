# KEPaymentTracker — Comprehensive System & Agent Handover Document

> **Document Version:** 2.0  
> **Updated:** November 2026  
> **Codebase Paths:**  
> - **Frontend (React 18 + TS + Vite):** `D:\PaymentTracker-Frontend`  
> - **Backend (ASP.NET Core 10 Web API):** `D:\paymenttracker-backend`  
> - **Live Render Cloud Backend:** `https://sitepaymenttracker-backend.onrender.com`  
> - **Live Cloud Database:** Supabase PostgreSQL 17 (`hbstkhenoocozepiusmw`, Region: `ap-southeast-2`)  
> **Target Audience:** Incoming AI Agents, Software Engineers, and DevOps Engineers  

---

## 1. Executive Overview & System Purpose

**KEPaymentTracker** is an enterprise-grade construction payment, daily wage, and site expense tracking web application. Built for real-world construction infrastructure companies (e.g., *BuildCon Infra Ltd*), it addresses the operational challenges of managing multi-site field finances, labourer attendance, wage payouts, and expense compliance.

### Key Business Problems Solved
1. **Field Wage Exploitation Prevention**: Registered baseline daily wage rates are tracked and compared against payout requests. Discrepancies trigger automated audit warnings to prevent underpayment or wage diversion.
2. **Multi-Admin Governance**: Designed for 4 admins. Admin-initiated expense or wage requests require approval by another Admin, eliminating single-point unilateral payouts.
3. **Accountant Period Boundaries**: Accountants have read-only access restricted by default to a 1-year window, but can formally submit a **Period Access Request** to Admins for historical or future audits.
4. **Site Financial Health & Measurement Books (MB)**: Measurement Books record verified field construction milestones. Sites track contract value vs actual expenses. Site Profit/Loss calculations are withheld until the Admin marks the final client payment / final MB received.
5. **Real-Time Strict Expense Separation**: Unapproved ("Pending") payment requests are strictly quarantined and never pollute expense pie charts or financial summaries until formally approved.
6. **Multi-Identifier Field Login & Optional Email**: Construction supervisors and staff can log in using either their email address or a unique **6-digit User ID** (e.g. `521402`). Email is strictly optional for all non-admin employees (Supervisors, Accountants, Vendors).
7. **Admin Self-Exclusion**: Admins never see themselves listed in the manageable employee roster (`/api/users`), preventing accidental self-deactivation.

---

## 2. Cloud Architecture & Deployment Status

```
                           ┌────────────────────────────────────────┐
                           │         React 18 + Vite Frontend       │
                           │   Local: http://localhost:5173         │
                           │   Cloud: Vercel / Netlify / Render     │
                           └───────────────────┬────────────────────┘
                                               │ HTTPS / JSON
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

---

## 3. Database & Supabase Configuration

### Supabase Project Details
- **Project Reference ID**: `hbstkhenoocozepiusmw`
- **Region / Zone**: `ap-southeast-2` (AWS Sydney)
- **Database Name**: `postgres`
- **User**: `postgres.hbstkhenoocozepiusmw`
- **Password**: `Usman$887517`
- **Connection Pooler Host (IPv4)**: `aws-0-ap-southeast-2.pooler.supabase.com:5432`

> [!IMPORTANT]
> **Render IPv6 Gotcha**: Render free instances do not support outbound IPv6. Direct connections to `db.hbstkhenoocozepiusmw.supabase.co` fail with `SocketException: Network is unreachable`. Always route through the **Supabase IPv4 Connection Pooler** (`aws-0-ap-southeast-2.pooler.supabase.com:5432`) with username `postgres.<project-id>`.

---

## 4. Test Credentials & Roster

All seeded test accounts use the default password: **`Password123!`**

| Role | Name | 6-Digit ID | Email | Notes |
| :--- | :--- | :--- | :--- | :--- |
| **Admin** | Shaquib Khan | `749201` | `khanshaquib21@gmail.com` | Tenant Admin, email mandatory |
| **Accountant** | Ramesh Sharma | `521402` | *(None - Optional)* | Pure 6-digit ID login |
| **Supervisor** | Surendra Kumar | `616244` | `surendra@buildcon.in` | Assigned to Metro Station Project |
| **Supervisor** | Suresh Verma | `349104` | `suresh.verma@buildcon.in` | Assigned to Highway Bypass Project |
| **Supervisor** | Amit Patel | `839201` | *(None - Optional)* | Pure 6-digit ID login |
| **Vendor** | Apex Supplies | `918234` | `orders@apexsupplies.in` | Material supplier vendor account |

---

## 5. Dockerfile & Render Gotchas

1. **Non-Root Docker User File Permissions (`APP_UID`)**:
   - The official Microsoft ASP.NET runtime image runs as a non-root user `app` (`UID 1654`).
   - If files or directories in `/app/Storage` are created at runtime, `UnauthorizedAccessException` is thrown unless the directory permissions are explicitly set.
   - The `Dockerfile` creates `/app/Storage/Reports` and `/app/Logs` with `chmod -R 775` and `chown -R $APP_UID:$APP_UID`.
   - The C# codebase also contains a graceful fallback in `ExportBackgroundWorker.cs` and `ReportsController.cs` to write exports to `Path.GetTempPath()` (`/tmp`) if `/app/Storage` is read-only.
2. **Dynamic Port Binding**:
   - Render injects the `$PORT` environment variable at runtime.
   - The application binds dynamically to `http://0.0.0.0:${PORT:-8080}` via `Program.cs`.
3. **CORS Configuration**:
   - Wildcard CORS with credentials is fully enabled on the backend so any deployed frontend or local dev server can call the Render backend directly.

---

## 6. Frontend Configuration

- **Location**: `D:\PaymentTracker-Frontend`
- **Environment Files**:
  - `.env.production`: `VITE_API_URL=https://sitepaymenttracker-backend.onrender.com/api`
  - `.env.example`: Template for local vs cloud backend toggles.
- **Vite Proxy (Local)**:
  - In `vite.config.ts`, `/api` proxies to `http://localhost:5000` when running locally.
- **Client Configuration (`src/core/api/client.ts`)**:
  - Reads `import.meta.env.VITE_API_URL || '/api'`.
  - Type definitions are provided in `src/vite-env.d.ts`.
