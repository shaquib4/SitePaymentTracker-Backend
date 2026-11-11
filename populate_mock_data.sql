-- ========================================================================
-- KEPaymentTracker Database Mock Data Seed Script
-- Populates: Users, Sites, Labourers, PaymentRequests, PaymentAuditLogs, MeasurementBooks
-- ========================================================================

BEGIN;

-- 1. Sites Setup (Ensure contract values & locations)
UPDATE "Sites"
SET "ContractValue" = 5000000.00, "Location" = 'Sector 62, Noida', "IsActive" = TRUE
WHERE "Id" = '33333333-3333-3333-3333-333333333333';

UPDATE "Sites"
SET "ContractValue" = 8500000.00, "Location" = 'Whitefield, Bengaluru', "IsActive" = TRUE
WHERE "Id" = '44444444-4444-4444-4444-444444444444';

INSERT INTO "Sites" ("Id", "TenantId", "Name", "Location", "ContractValue", "FinalPaymentReceived", "IsSettled", "IsActive", "CreatedAtUtc")
VALUES 
('55555555-5555-5555-5555-555555555555', '11111111-1111-1111-1111-111111111111', 'Expressway Flyover Bridge - Pier 14', 'NH-48, Gurugram', 3200000.00, NULL, FALSE, TRUE, NOW())
ON CONFLICT ("Id") DO UPDATE SET "ContractValue" = 3200000.00, "IsActive" = TRUE;

-- 2. Users (Accountant with optional NULL email, Supervisor with optional NULL email, Vendor)
-- Password for all is Password123! ($2a$11$ooRMTtAZRiOqLjebkXH9Du5zXm.nJzic3UDSHKWyW/0QxhQ.ceGfC)
INSERT INTO "Users" ("Id", "TenantId", "FullName", "Email", "SixDigitUserId", "PhoneNumber", "Role", "Department", "PasswordHash", "IsActive", "CreatedAtUtc")
VALUES
-- Accountant (No email provided - demonstrates optional email!)
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', '11111111-1111-1111-1111-111111111111', 'Ramesh Sharma', NULL, '521402', '+919812345678', 1, 'Finance & Audit', '$2a$11$ooRMTtAZRiOqLjebkXH9Du5zXm.nJzic3UDSHKWyW/0QxhQ.ceGfC', TRUE, NOW() - INTERVAL '40 days'),
-- Supervisor Amit Patel (No email provided - demonstrates optional email!)
('cccccccc-cccc-cccc-cccc-cccccccccccc', '11111111-1111-1111-1111-111111111111', 'Amit Patel', NULL, '839201', '+919876501234', 2, 'Civil & Earthworks', '$2a$11$ooRMTtAZRiOqLjebkXH9Du5zXm.nJzic3UDSHKWyW/0QxhQ.ceGfC', TRUE, NOW() - INTERVAL '35 days'),
-- Vendor
('dddddddd-dddd-dddd-dddd-dddddddddddd', '11111111-1111-1111-1111-111111111111', 'Apex Steel & Cement Suppliers', 'orders@apexsupplies.in', '918234', '+919988776655', 3, 'Procurement', '$2a$11$ooRMTtAZRiOqLjebkXH9Du5zXm.nJzic3UDSHKWyW/0QxhQ.ceGfC', TRUE, NOW() - INTERVAL '30 days')
ON CONFLICT ("Id") DO UPDATE 
SET "FullName" = EXCLUDED."FullName", "Email" = EXCLUDED."Email", "PasswordHash" = EXCLUDED."PasswordHash", "IsActive" = TRUE;

-- 3. Labourers Setup
INSERT INTO "Labourers" ("Id", "TenantId", "Name", "DailyWageRate", "SupervisorId", "SiteId", "CreatedByUserId", "IsActive", "IsDeleted", "CreatedAtUtc")
VALUES
('10000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', 'Radhe Shyam Yadav', 600.00, '4fd8710e-ce13-4110-b244-832e30dc6262', '33333333-3333-3333-3333-333333333333', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', TRUE, FALSE, NOW() - INTERVAL '30 days'),
('10000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', 'Dinesh Kumar Mondal', 650.00, '4fd8710e-ce13-4110-b244-832e30dc6262', '33333333-3333-3333-3333-333333333333', '4fd8710e-ce13-4110-b244-832e30dc6262', TRUE, FALSE, NOW() - INTERVAL '25 days'),
('10000000-0000-0000-0000-000000000003', '11111111-1111-1111-1111-111111111111', 'Mukesh Manjhi', 550.00, '3476fada-48cd-4a23-9887-4cf401aa1f1a', '44444444-4444-4444-4444-444444444444', '3476fada-48cd-4a23-9887-4cf401aa1f1a', TRUE, FALSE, NOW() - INTERVAL '20 days'),
('10000000-0000-0000-0000-000000000004', '11111111-1111-1111-1111-111111111111', 'Santosh Paswan', 700.00, 'cccccccc-cccc-cccc-cccc-cccccccccccc', '55555555-5555-5555-5555-555555555555', 'cccccccc-cccc-cccc-cccc-cccccccccccc', TRUE, FALSE, NOW() - INTERVAL '15 days'),
('10000000-0000-0000-0000-000000000005', '11111111-1111-1111-1111-111111111111', 'Sonu Kumar Shah', 500.00, 'cccccccc-cccc-cccc-cccc-cccccccccccc', '55555555-5555-5555-5555-555555555555', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', TRUE, FALSE, NOW() - INTERVAL '10 days')
ON CONFLICT ("Id") DO UPDATE 
SET "Name" = EXCLUDED."Name", "DailyWageRate" = EXCLUDED."DailyWageRate", "SupervisorId" = EXCLUDED."SupervisorId", "SiteId" = EXCLUDED."SiteId", "IsActive" = TRUE;

-- 4. Payment Requests (Expenses across all roles)
-- Clear previous sample payments if any
DELETE FROM "PaymentAuditLogs";
DELETE FROM "PaymentRequests";

-- 4.1 Labour Wage Payout (Approved - 12 days * 600 = 7,200)
INSERT INTO "PaymentRequests" (
    "Id", "TenantId", "UserId", "CategoryId", "Category", "Amount", "ExpenseYear",
    "StartDate", "EndDate", "RequestDate", "Status", "LabourerId", "SiteId",
    "Title", "Description", "ApprovalComments", "Currency", "CreatedAtUtc", "UpdatedAtUtc", "Version"
) VALUES (
    '20000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111',
    '4fd8710e-ce13-4110-b244-832e30dc6262', '6c21b416-8129-4d92-a152-6e65075f7171', 'Labour Wages',
    7200.00, 2026, '2026-10-15', '2026-10-27', NOW() - INTERVAL '12 days',
    2, '10000000-0000-0000-0000-000000000001', '33333333-3333-3333-3333-333333333333',
    'Bi-Weekly Wage: Radhe Shyam Yadav', '12 days bar-bending & shuttering works',
    'Verified muster roll with site engineer.', 'INR', NOW() - INTERVAL '12 days', NOW() - INTERVAL '11 days', 1
);

-- 4.2 Labour Wage Payout (Completed/Paid - 6 days * 550 = 3,300)
INSERT INTO "PaymentRequests" (
    "Id", "TenantId", "UserId", "CategoryId", "Category", "Amount", "ExpenseYear",
    "StartDate", "EndDate", "RequestDate", "Status", "LabourerId", "SiteId",
    "Title", "Description", "ApprovalComments", "Currency", "CreatedAtUtc", "UpdatedAtUtc", "Version"
) VALUES (
    '20000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111',
    '3476fada-48cd-4a23-9887-4cf401aa1f1a', '6c21b416-8129-4d92-a152-6e65075f7171', 'Labour Wages',
    3300.00, 2026, '2026-10-20', '2026-10-25', NOW() - INTERVAL '10 days',
    4, '10000000-0000-0000-0000-000000000003', '44444444-4444-4444-4444-444444444444',
    'Weekly Wage: Mukesh Manjhi', '6 days excavation and slab casting work',
    'Paid via direct NEFT to labourer account.', 'INR', NOW() - INTERVAL '10 days', NOW() - INTERVAL '9 days', 1
);

-- 4.3 Supervisor Diesel & Fuel (Approved - 31,500)
INSERT INTO "PaymentRequests" (
    "Id", "TenantId", "UserId", "CategoryId", "Category", "Amount", "ExpenseYear",
    "StartDate", "EndDate", "RequestDate", "Status", "LabourerId", "SiteId",
    "Title", "Description", "ApprovalComments", "Currency", "CreatedAtUtc", "UpdatedAtUtc", "Version"
) VALUES (
    '20000000-0000-0000-0000-000000000003', '11111111-1111-1111-1111-111111111111',
    '4fd8710e-ce13-4110-b244-832e30dc6262', 'f35b9ca5-5768-4838-b10e-975efe5cc4bb', 'Diesel & Fuel',
    31500.00, 2026, '2026-11-01', '2026-11-02', NOW() - INTERVAL '6 days',
    2, NULL, '33333333-3333-3333-3333-333333333333',
    'Diesel for Site Generator & Excavator', '350 Litres HSD fuel for continuous night shift concreting',
    'Indian Oil bill #IOC-9921 attached and verified.', 'INR', NOW() - INTERVAL '6 days', NOW() - INTERVAL '5 days', 1
);

-- 4.4 Supervisor Equipment Rental (Approved - 65,000)
INSERT INTO "PaymentRequests" (
    "Id", "TenantId", "UserId", "CategoryId", "Category", "Amount", "ExpenseYear",
    "StartDate", "EndDate", "RequestDate", "Status", "LabourerId", "SiteId",
    "Title", "Description", "ApprovalComments", "Currency", "CreatedAtUtc", "UpdatedAtUtc", "Version"
) VALUES (
    '20000000-0000-0000-0000-000000000004', '11111111-1111-1111-1111-111111111111',
    'cccccccc-cccc-cccc-cccc-cccccccccccc', '1473f1ad-e1c0-4dd0-aa51-0fe9b00945f4', 'Equipment Rental',
    65000.00, 2026, '2026-10-18', '2026-10-20', NOW() - INTERVAL '15 days',
    2, NULL, '55555555-5555-5555-5555-555555555555',
    'Hydraulic Crane 50T Hire (3 Days)', 'Girder erection for span 3 and pier 14 placement',
    'Crane log sheet signed off by senior site engineer.', 'INR', NOW() - INTERVAL '15 days', NOW() - INTERVAL '14 days', 1
);

-- 4.5 Accountant Professional Fee Request (Approved - 45,000)
INSERT INTO "PaymentRequests" (
    "Id", "TenantId", "UserId", "CategoryId", "Category", "Amount", "ExpenseYear",
    "StartDate", "EndDate", "RequestDate", "Status", "LabourerId", "SiteId",
    "Title", "Description", "ApprovalComments", "Currency", "CreatedAtUtc", "UpdatedAtUtc", "Version"
) VALUES (
    '20000000-0000-0000-0000-000000000005', '11111111-1111-1111-1111-111111111111',
    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'f1bd7ed0-4fc7-459e-829d-0982f9ff229c', 'Salary',
    45000.00, 2026, '2026-10-01', '2026-10-31', NOW() - INTERVAL '7 days',
    2, NULL, NULL,
    'October 2026 Professional Accounting Fee', 'Monthly retainer for multi-site GST filing, audit prep & payroll ledger',
    'Approved by Admin Shaquib Khan.', 'INR', NOW() - INTERVAL '7 days', NOW() - INTERVAL '6 days', 1
);

-- 4.6 Vendor Material Purchase (Approved - 195,000)
INSERT INTO "PaymentRequests" (
    "Id", "TenantId", "UserId", "CategoryId", "Category", "Amount", "ExpenseYear",
    "StartDate", "EndDate", "RequestDate", "Status", "LabourerId", "SiteId",
    "Title", "Description", "ApprovalComments", "Currency", "CreatedAtUtc", "UpdatedAtUtc", "Version"
) VALUES (
    '20000000-0000-0000-0000-000000000006', '11111111-1111-1111-1111-111111111111',
    'dddddddd-dddd-dddd-dddd-dddddddddddd', '859a92c4-b9a7-4f3e-8af6-361ce901d32d', 'Cement',
    195000.00, 2026, '2026-10-22', '2026-10-23', NOW() - INTERVAL '14 days',
    2, NULL, '33333333-3333-3333-3333-333333333333',
    '500 Bags OPC 53 Grade Ultratech Cement', 'Invoice #APX-2026-1082 for Pier foundation batching',
    'Batching slip and store receipt pass verified.', 'INR', NOW() - INTERVAL '14 days', NOW() - INTERVAL '13 days', 1
);

-- 4.7 Labour Wage Payout (PENDING - 9,800. Must NOT appear in pie charts!)
INSERT INTO "PaymentRequests" (
    "Id", "TenantId", "UserId", "CategoryId", "Category", "Amount", "ExpenseYear",
    "StartDate", "EndDate", "RequestDate", "Status", "LabourerId", "SiteId",
    "Title", "Description", "ApprovalComments", "Currency", "CreatedAtUtc", "UpdatedAtUtc", "Version"
) VALUES (
    '20000000-0000-0000-0000-000000000007', '11111111-1111-1111-1111-111111111111',
    'cccccccc-cccc-cccc-cccc-cccccccccccc', '6c21b416-8129-4d92-a152-6e65075f7171', 'Labour Wages',
    9800.00, 2026, '2026-11-01', '2026-11-06', NOW() - INTERVAL '1 day',
    0, '10000000-0000-0000-0000-000000000004', '55555555-5555-5555-5555-555555555555',
    'Labour Wage Payout: Santosh Paswan', '14 days scaffolding setup on pier 14',
    NULL, 'INR', NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day', 1
);

-- 4.8 Supervisor Logistics Reimbursement (PENDING - 4,200. Must NOT appear in pie charts!)
INSERT INTO "PaymentRequests" (
    "Id", "TenantId", "UserId", "CategoryId", "Category", "Amount", "ExpenseYear",
    "StartDate", "EndDate", "RequestDate", "Status", "LabourerId", "SiteId",
    "Title", "Description", "ApprovalComments", "Currency", "CreatedAtUtc", "UpdatedAtUtc", "Version"
) VALUES (
    '20000000-0000-0000-0000-000000000008', '11111111-1111-1111-1111-111111111111',
    '3476fada-48cd-4a23-9887-4cf401aa1f1a', 'ec8a4852-e2ad-4171-8b4e-907af2677ed0', 'Transport & Logistics',
    4200.00, 2026, '2026-11-05', '2026-11-05', NOW() - INTERVAL '2 days',
    0, NULL, '44444444-4444-4444-4444-444444444444',
    'Tempo Freight Charges for Scaffolding Pipes', 'Emergency local transport of 120 steel pipes from central warehouse',
    NULL, 'INR', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', 1
);

-- 5. Payment Audit Logs (Immutable audit tracking)
INSERT INTO "PaymentAuditLogs" ("Id", "TenantId", "PaymentRequestId", "PreviousStatus", "NewStatus", "Action", "Comments", "PerformedByUserId", "PerformedByUserName", "TimestampUtc")
VALUES
(gen_random_uuid(), '11111111-1111-1111-1111-111111111111', '20000000-0000-0000-0000-000000000001', 0, 2, 'Approved', 'Verified muster roll with site engineer.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Shaquib Khan', NOW() - INTERVAL '11 days'),
(gen_random_uuid(), '11111111-1111-1111-1111-111111111111', '20000000-0000-0000-0000-000000000002', 2, 4, 'Completed', 'Paid via direct NEFT to labourer account.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Shaquib Khan', NOW() - INTERVAL '9 days'),
(gen_random_uuid(), '11111111-1111-1111-1111-111111111111', '20000000-0000-0000-0000-000000000003', 0, 2, 'Approved', 'Indian Oil bill #IOC-9921 verified.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Shaquib Khan', NOW() - INTERVAL '5 days'),
(gen_random_uuid(), '11111111-1111-1111-1111-111111111111', '20000000-0000-0000-0000-000000000004', 0, 2, 'Approved', 'Crane log sheet approved.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Shaquib Khan', NOW() - INTERVAL '14 days'),
(gen_random_uuid(), '11111111-1111-1111-1111-111111111111', '20000000-0000-0000-0000-000000000005', 0, 2, 'Approved', 'Approved by Admin Shaquib Khan.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Shaquib Khan', NOW() - INTERVAL '6 days'),
(gen_random_uuid(), '11111111-1111-1111-1111-111111111111', '20000000-0000-0000-0000-000000000006', 0, 2, 'Approved', 'Batching slip & gate pass verified.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Shaquib Khan', NOW() - INTERVAL '13 days'),
(gen_random_uuid(), '11111111-1111-1111-1111-111111111111', '20000000-0000-0000-0000-000000000007', 0, 0, 'Created', 'Submitted by supervisor Amit Patel.', 'cccccccc-cccc-cccc-cccc-cccccccccccc', 'Amit Patel', NOW() - INTERVAL '1 day'),
(gen_random_uuid(), '11111111-1111-1111-1111-111111111111', '20000000-0000-0000-0000-000000000008', 0, 0, 'Created', 'Submitted by supervisor Suresh.', '3476fada-48cd-4a23-9887-4cf401aa1f1a', 'Suresh', NOW() - INTERVAL '2 days');

-- 6. Measurement Books (MB)
INSERT INTO "MeasurementBooks" ("Id", "TenantId", "SiteId", "Month", "Year", "TotalEstimatedValue", "ExcelFileUrl", "UploadedAt")
VALUES
(gen_random_uuid(), '11111111-1111-1111-1111-111111111111', '33333333-3333-3333-3333-333333333333', 10, 2026, 1250000.00, 'mb_metro_phase4_oct2026.xlsx', NOW() - INTERVAL '10 days'),
(gen_random_uuid(), '11111111-1111-1111-1111-111111111111', '44444444-4444-4444-4444-444444444444', 10, 2026, 2100000.00, 'mb_skyline_oct2026.xlsx', NOW() - INTERVAL '8 days')
ON CONFLICT ("Id") DO NOTHING;

COMMIT;

