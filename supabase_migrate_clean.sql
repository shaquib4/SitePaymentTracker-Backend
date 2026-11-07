--
-- PostgreSQL database dump
--


-- Dumped from database version 18.3
-- Dumped by pg_dump version 18.3

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: public; Type: SCHEMA; Schema: -; Owner: -
--



--
-- Name: SCHEMA public; Type: COMMENT; Schema: -; Owner: -
--



SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: Categories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Categories" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: Labourers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Labourers" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "SupervisorId" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "PhotoUrl" text,
    "DailyWageRate" numeric(14,2) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CreatedByUserId" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "SiteId" uuid
);


--
-- Name: MeasurementBooks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."MeasurementBooks" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "SiteId" uuid NOT NULL,
    "Month" integer NOT NULL,
    "Year" integer NOT NULL,
    "ExcelFileUrl" text,
    "TotalEstimatedValue" numeric(14,2) NOT NULL,
    "UploadedAt" timestamp with time zone NOT NULL
);


--
-- Name: PaymentAuditLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PaymentAuditLogs" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "PaymentRequestId" uuid NOT NULL,
    "Action" character varying(50) NOT NULL,
    "PreviousStatus" integer,
    "NewStatus" integer NOT NULL,
    "PerformedByUserId" uuid NOT NULL,
    "PerformedByUserName" character varying(100) NOT NULL,
    "TimestampUtc" timestamp with time zone NOT NULL,
    "Comments" character varying(500)
);


--
-- Name: PaymentRequests; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PaymentRequests" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "CategoryId" uuid,
    "Category" character varying(100) NOT NULL,
    "LabourerId" uuid,
    "SiteId" uuid,
    "Title" character varying(150) NOT NULL,
    "Description" character varying(1000) NOT NULL,
    "Amount" numeric(14,2) NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "ExpenseYear" integer NOT NULL,
    "StartDate" date NOT NULL,
    "EndDate" date NOT NULL,
    "RequestDate" timestamp with time zone NOT NULL,
    "Status" integer NOT NULL,
    "InvoiceNumber" text,
    "ReceiptAttachmentPath" text,
    "RejectionReason" text,
    "InfoRequestDetails" text,
    "ApprovalComments" text,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "Version" bigint NOT NULL
);


--
-- Name: PeriodAccessRequests; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PeriodAccessRequests" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "RequestedByUserId" uuid NOT NULL,
    "TargetYear" integer NOT NULL,
    "Reason" text NOT NULL,
    "Status" character varying(50) DEFAULT 'Pending'::character varying NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "ReviewedByUserId" uuid,
    "ReviewedAtUtc" timestamp with time zone,
    "AdminNotes" text
);


--
-- Name: Sites; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Sites" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Name" character varying(150) NOT NULL,
    "Location" character varying(250) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "ContractValue" numeric(14,2),
    "FinalPaymentReceived" numeric(14,2),
    "IsSettled" boolean DEFAULT false NOT NULL,
    "SettledAtUtc" timestamp with time zone
);


--
-- Name: Tenants; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Tenants" (
    "Id" uuid NOT NULL,
    "Name" character varying(150) NOT NULL,
    "SubscriptionPlan" character varying(50) NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: Users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Users" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Email" character varying(150),
    "FullName" character varying(100) NOT NULL,
    "PhoneNumber" character varying(20) NOT NULL,
    "SixDigitUserId" character varying(6) NOT NULL,
    "PasswordHash" text NOT NULL,
    "Role" integer NOT NULL,
    "Department" character varying(100) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


--
-- Data for Name: Categories; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Categories" ("Id", "TenantId", "Name", "IsActive", "CreatedAtUtc") FROM stdin;
859a92c4-b9a7-4f3e-8af6-361ce901d32d	11111111-1111-1111-1111-111111111111	Cement	t	2026-11-07 17:35:53.157145+05:30
2cc372c8-a4ae-403e-9485-9037eb9b3488	11111111-1111-1111-1111-111111111111	Sariya (Rebar)	t	2026-11-07 17:35:53.157145+05:30
6c21b416-8129-4d92-a152-6e65075f7171	11111111-1111-1111-1111-111111111111	Labour Wages	t	2026-11-07 17:35:53.157145+05:30
ec8a4852-e2ad-4171-8b4e-907af2677ed0	11111111-1111-1111-1111-111111111111	Transport & Logistics	t	2026-11-07 17:35:53.157145+05:30
f35b9ca5-5768-4838-b10e-975efe5cc4bb	11111111-1111-1111-1111-111111111111	Diesel & Fuel	t	2026-11-07 17:35:53.157145+05:30
1473f1ad-e1c0-4dd0-aa51-0fe9b00945f4	11111111-1111-1111-1111-111111111111	Equipment Rental	t	2026-11-07 17:35:53.157145+05:30
f1bd7ed0-4fc7-459e-829d-0982f9ff229c	11111111-1111-1111-1111-111111111111	Salary	t	2026-11-07 17:35:53.157145+05:30
\.


--
-- Data for Name: Labourers; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Labourers" ("Id", "TenantId", "SupervisorId", "Name", "PhotoUrl", "DailyWageRate", "IsActive", "CreatedAtUtc", "CreatedByUserId", "IsDeleted", "SiteId") FROM stdin;
10000000-0000-0000-0000-000000000001	11111111-1111-1111-1111-111111111111	4fd8710e-ce13-4110-b244-832e30dc6262	Radhe Shyam Yadav	\N	600.00	t	2026-10-08 19:59:55.708401+05:30	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa	f	33333333-3333-3333-3333-333333333333
10000000-0000-0000-0000-000000000002	11111111-1111-1111-1111-111111111111	4fd8710e-ce13-4110-b244-832e30dc6262	Dinesh Kumar Mondal	\N	650.00	t	2026-10-13 19:59:55.708401+05:30	4fd8710e-ce13-4110-b244-832e30dc6262	f	33333333-3333-3333-3333-333333333333
10000000-0000-0000-0000-000000000003	11111111-1111-1111-1111-111111111111	3476fada-48cd-4a23-9887-4cf401aa1f1a	Mukesh Manjhi	\N	550.00	t	2026-10-18 19:59:55.708401+05:30	3476fada-48cd-4a23-9887-4cf401aa1f1a	f	44444444-4444-4444-4444-444444444444
10000000-0000-0000-0000-000000000004	11111111-1111-1111-1111-111111111111	cccccccc-cccc-cccc-cccc-cccccccccccc	Santosh Paswan	\N	700.00	t	2026-10-23 19:59:55.708401+05:30	cccccccc-cccc-cccc-cccc-cccccccccccc	f	55555555-5555-5555-5555-555555555555
10000000-0000-0000-0000-000000000005	11111111-1111-1111-1111-111111111111	cccccccc-cccc-cccc-cccc-cccccccccccc	Sonu Kumar Shah	\N	500.00	t	2026-10-28 19:59:55.708401+05:30	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa	f	55555555-5555-5555-5555-555555555555
\.


--
-- Data for Name: MeasurementBooks; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."MeasurementBooks" ("Id", "TenantId", "SiteId", "Month", "Year", "ExcelFileUrl", "TotalEstimatedValue", "UploadedAt") FROM stdin;
4836d0a6-bed8-4eb0-a3a2-a2b76376e782	11111111-1111-1111-1111-111111111111	33333333-3333-3333-3333-333333333333	10	2026	mb_metro_phase4_oct2026.xlsx	1250000.00	2026-10-28 19:59:55.708401+05:30
b9c150c3-de30-4b74-b9e4-c8498ac6a69e	11111111-1111-1111-1111-111111111111	44444444-4444-4444-4444-444444444444	10	2026	mb_skyline_oct2026.xlsx	2100000.00	2026-10-30 19:59:55.708401+05:30
\.


--
-- Data for Name: PaymentAuditLogs; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."PaymentAuditLogs" ("Id", "TenantId", "PaymentRequestId", "Action", "PreviousStatus", "NewStatus", "PerformedByUserId", "PerformedByUserName", "TimestampUtc", "Comments") FROM stdin;
9c67198e-eaa9-4949-bef3-4f9925e40277	11111111-1111-1111-1111-111111111111	20000000-0000-0000-0000-000000000001	Approved	0	2	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa	Shaquib Khan	2026-10-27 19:59:55.708401+05:30	Verified muster roll with site engineer.
6a5dba0d-90fa-449f-a5cb-f0536890fb82	11111111-1111-1111-1111-111111111111	20000000-0000-0000-0000-000000000002	Completed	2	4	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa	Shaquib Khan	2026-10-29 19:59:55.708401+05:30	Paid via direct NEFT to labourer account.
b31c560f-17de-463e-be27-8159a2abbb0d	11111111-1111-1111-1111-111111111111	20000000-0000-0000-0000-000000000003	Approved	0	2	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa	Shaquib Khan	2026-11-02 19:59:55.708401+05:30	Indian Oil bill #IOC-9921 verified.
3cbb9280-468d-4bfa-b5ff-adba85d3b4fc	11111111-1111-1111-1111-111111111111	20000000-0000-0000-0000-000000000004	Approved	0	2	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa	Shaquib Khan	2026-10-24 19:59:55.708401+05:30	Crane log sheet approved.
7895db4f-df0d-4276-8618-19808307dcfa	11111111-1111-1111-1111-111111111111	20000000-0000-0000-0000-000000000005	Approved	0	2	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa	Shaquib Khan	2026-11-01 19:59:55.708401+05:30	Approved by Admin Shaquib Khan.
664311eb-29b0-453f-9c65-c313c68a56be	11111111-1111-1111-1111-111111111111	20000000-0000-0000-0000-000000000006	Approved	0	2	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa	Shaquib Khan	2026-10-25 19:59:55.708401+05:30	Batching slip & gate pass verified.
90406854-a490-4598-b5a9-b0f629f8dc4b	11111111-1111-1111-1111-111111111111	20000000-0000-0000-0000-000000000007	Created	0	0	cccccccc-cccc-cccc-cccc-cccccccccccc	Amit Patel	2026-11-06 19:59:55.708401+05:30	Submitted by supervisor Amit Patel.
45f10e8d-16b5-4a73-91ed-6f2e3695f633	11111111-1111-1111-1111-111111111111	20000000-0000-0000-0000-000000000008	Created	0	0	3476fada-48cd-4a23-9887-4cf401aa1f1a	Suresh	2026-11-05 19:59:55.708401+05:30	Submitted by supervisor Suresh.
\.


--
-- Data for Name: PaymentRequests; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."PaymentRequests" ("Id", "TenantId", "UserId", "CategoryId", "Category", "LabourerId", "SiteId", "Title", "Description", "Amount", "Currency", "ExpenseYear", "StartDate", "EndDate", "RequestDate", "Status", "InvoiceNumber", "ReceiptAttachmentPath", "RejectionReason", "InfoRequestDetails", "ApprovalComments", "CreatedAtUtc", "UpdatedAtUtc", "Version") FROM stdin;
20000000-0000-0000-0000-000000000001	11111111-1111-1111-1111-111111111111	4fd8710e-ce13-4110-b244-832e30dc6262	6c21b416-8129-4d92-a152-6e65075f7171	Labour Wages	10000000-0000-0000-0000-000000000001	33333333-3333-3333-3333-333333333333	Bi-Weekly Wage: Radhe Shyam Yadav	12 days bar-bending & shuttering works	7200.00	INR	2026	2026-10-15	2026-10-27	2026-10-26 19:59:55.708401+05:30	2	\N	\N	\N	\N	Verified muster roll with site engineer.	2026-10-26 19:59:55.708401+05:30	2026-10-27 19:59:55.708401+05:30	1
20000000-0000-0000-0000-000000000002	11111111-1111-1111-1111-111111111111	3476fada-48cd-4a23-9887-4cf401aa1f1a	6c21b416-8129-4d92-a152-6e65075f7171	Labour Wages	10000000-0000-0000-0000-000000000003	44444444-4444-4444-4444-444444444444	Weekly Wage: Mukesh Manjhi	6 days excavation and slab casting work	3300.00	INR	2026	2026-10-20	2026-10-25	2026-10-28 19:59:55.708401+05:30	4	\N	\N	\N	\N	Paid via direct NEFT to labourer account.	2026-10-28 19:59:55.708401+05:30	2026-10-29 19:59:55.708401+05:30	1
20000000-0000-0000-0000-000000000003	11111111-1111-1111-1111-111111111111	4fd8710e-ce13-4110-b244-832e30dc6262	f35b9ca5-5768-4838-b10e-975efe5cc4bb	Diesel & Fuel	\N	33333333-3333-3333-3333-333333333333	Diesel for Site Generator & Excavator	350 Litres HSD fuel for continuous night shift concreting	31500.00	INR	2026	2026-11-01	2026-11-02	2026-11-01 19:59:55.708401+05:30	2	\N	\N	\N	\N	Indian Oil bill #IOC-9921 attached and verified.	2026-11-01 19:59:55.708401+05:30	2026-11-02 19:59:55.708401+05:30	1
20000000-0000-0000-0000-000000000004	11111111-1111-1111-1111-111111111111	cccccccc-cccc-cccc-cccc-cccccccccccc	1473f1ad-e1c0-4dd0-aa51-0fe9b00945f4	Equipment Rental	\N	55555555-5555-5555-5555-555555555555	Hydraulic Crane 50T Hire (3 Days)	Girder erection for span 3 and pier 14 placement	65000.00	INR	2026	2026-10-18	2026-10-20	2026-10-23 19:59:55.708401+05:30	2	\N	\N	\N	\N	Crane log sheet signed off by senior site engineer.	2026-10-23 19:59:55.708401+05:30	2026-10-24 19:59:55.708401+05:30	1
20000000-0000-0000-0000-000000000005	11111111-1111-1111-1111-111111111111	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	f1bd7ed0-4fc7-459e-829d-0982f9ff229c	Salary	\N	\N	October 2026 Professional Accounting Fee	Monthly retainer for multi-site GST filing, audit prep & payroll ledger	45000.00	INR	2026	2026-10-01	2026-10-31	2026-10-31 19:59:55.708401+05:30	2	\N	\N	\N	\N	Approved by Admin Shaquib Khan.	2026-10-31 19:59:55.708401+05:30	2026-11-01 19:59:55.708401+05:30	1
20000000-0000-0000-0000-000000000006	11111111-1111-1111-1111-111111111111	dddddddd-dddd-dddd-dddd-dddddddddddd	859a92c4-b9a7-4f3e-8af6-361ce901d32d	Cement	\N	33333333-3333-3333-3333-333333333333	500 Bags OPC 53 Grade Ultratech Cement	Invoice #APX-2026-1082 for Pier foundation batching	195000.00	INR	2026	2026-10-22	2026-10-23	2026-10-24 19:59:55.708401+05:30	2	\N	\N	\N	\N	Batching slip and store receipt pass verified.	2026-10-24 19:59:55.708401+05:30	2026-10-25 19:59:55.708401+05:30	1
20000000-0000-0000-0000-000000000007	11111111-1111-1111-1111-111111111111	cccccccc-cccc-cccc-cccc-cccccccccccc	6c21b416-8129-4d92-a152-6e65075f7171	Labour Wages	10000000-0000-0000-0000-000000000004	55555555-5555-5555-5555-555555555555	Labour Wage Payout: Santosh Paswan	14 days scaffolding setup on pier 14	9800.00	INR	2026	2026-11-01	2026-11-06	2026-11-06 19:59:55.708401+05:30	0	\N	\N	\N	\N	\N	2026-11-06 19:59:55.708401+05:30	2026-11-06 19:59:55.708401+05:30	0
20000000-0000-0000-0000-000000000008	11111111-1111-1111-1111-111111111111	3476fada-48cd-4a23-9887-4cf401aa1f1a	ec8a4852-e2ad-4171-8b4e-907af2677ed0	Transport & Logistics	\N	44444444-4444-4444-4444-444444444444	Tempo Freight Charges for Scaffolding Pipes	Emergency local transport of 120 steel pipes from central warehouse	4200.00	INR	2026	2026-11-05	2026-11-05	2026-11-05 19:59:55.708401+05:30	0	\N	\N	\N	\N	\N	2026-11-05 19:59:55.708401+05:30	2026-11-05 19:59:55.708401+05:30	0
\.


--
-- Data for Name: PeriodAccessRequests; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."PeriodAccessRequests" ("Id", "TenantId", "RequestedByUserId", "TargetYear", "Reason", "Status", "CreatedAtUtc", "ReviewedByUserId", "ReviewedAtUtc", "AdminNotes") FROM stdin;
\.


--
-- Data for Name: Sites; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Sites" ("Id", "TenantId", "Name", "Location", "IsActive", "CreatedAtUtc", "ContractValue", "FinalPaymentReceived", "IsSettled", "SettledAtUtc") FROM stdin;
33333333-3333-3333-3333-333333333333	11111111-1111-1111-1111-111111111111	Metro Phase 4 - Station 12	Sector 62, Noida	t	2026-11-07 17:35:53.162057+05:30	5000000.00	\N	f	\N
44444444-4444-4444-4444-444444444444	11111111-1111-1111-1111-111111111111	Skyline Heights Tower B	Whitefield, Bengaluru	t	2026-11-07 17:35:53.162057+05:30	8500000.00	\N	f	\N
55555555-5555-5555-5555-555555555555	11111111-1111-1111-1111-111111111111	Expressway Flyover Bridge - Pier 14	NH-48, Gurugram	t	2026-11-07 19:59:55.708401+05:30	3200000.00	\N	f	\N
\.


--
-- Data for Name: Tenants; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Tenants" ("Id", "Name", "SubscriptionPlan", "Currency", "IsActive", "CreatedAtUtc") FROM stdin;
11111111-1111-1111-1111-111111111111	BuildCon Infra Ltd	Flat-30k-Yr	INR	t	2026-11-07 17:35:53.147539+05:30
\.


--
-- Data for Name: Users; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Users" ("Id", "TenantId", "Email", "FullName", "PhoneNumber", "SixDigitUserId", "PasswordHash", "Role", "Department", "IsActive", "CreatedAtUtc") FROM stdin;
aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa	11111111-1111-1111-1111-111111111111	khanshaquib21@gmail.com	Shaquib Khan	+919876543210	749201	$2a$11$ooRMTtAZRiOqLjebkXH9Du5zXm.nJzic3UDSHKWyW/0QxhQ.ceGfC	0	Executive Management	t	2026-11-07 17:35:53.151357+05:30
4fd8710e-ce13-4110-b244-832e30dc6262	11111111-1111-1111-1111-111111111111	xyz@gmail.com	Suredra	7890123456	616244	$2a$11$ooRMTtAZRiOqLjebkXH9Du5zXm.nJzic3UDSHKWyW/0QxhQ.ceGfC	2	Site Operations	t	2026-11-07 18:02:42.115314+05:30
3476fada-48cd-4a23-9887-4cf401aa1f1a	11111111-1111-1111-1111-111111111111	xyyz@gmail.com	Suresh	7890123456	349104	$2a$11$ooRMTtAZRiOqLjebkXH9Du5zXm.nJzic3UDSHKWyW/0QxhQ.ceGfC	2	Site Operations	t	2026-11-07 18:40:38.027637+05:30
bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	11111111-1111-1111-1111-111111111111	\N	Ramesh Sharma	+919812345678	521402	$2a$11$ooRMTtAZRiOqLjebkXH9Du5zXm.nJzic3UDSHKWyW/0QxhQ.ceGfC	1	Finance & Audit	t	2026-09-28 19:59:55.708401+05:30
cccccccc-cccc-cccc-cccc-cccccccccccc	11111111-1111-1111-1111-111111111111	\N	Amit Patel	+919876501234	839201	$2a$11$ooRMTtAZRiOqLjebkXH9Du5zXm.nJzic3UDSHKWyW/0QxhQ.ceGfC	2	Civil & Earthworks	t	2026-10-03 19:59:55.708401+05:30
dddddddd-dddd-dddd-dddd-dddddddddddd	11111111-1111-1111-1111-111111111111	orders@apexsupplies.in	Apex Steel & Cement Suppliers	+919988776655	918234	$2a$11$ooRMTtAZRiOqLjebkXH9Du5zXm.nJzic3UDSHKWyW/0QxhQ.ceGfC	3	Procurement	t	2026-10-08 19:59:55.708401+05:30
\.


--
-- Name: Categories PK_Categories; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Categories"
    ADD CONSTRAINT "PK_Categories" PRIMARY KEY ("Id");


--
-- Name: Labourers PK_Labourers; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Labourers"
    ADD CONSTRAINT "PK_Labourers" PRIMARY KEY ("Id");


--
-- Name: MeasurementBooks PK_MeasurementBooks; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."MeasurementBooks"
    ADD CONSTRAINT "PK_MeasurementBooks" PRIMARY KEY ("Id");


--
-- Name: PaymentAuditLogs PK_PaymentAuditLogs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PaymentAuditLogs"
    ADD CONSTRAINT "PK_PaymentAuditLogs" PRIMARY KEY ("Id");


--
-- Name: PaymentRequests PK_PaymentRequests; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PaymentRequests"
    ADD CONSTRAINT "PK_PaymentRequests" PRIMARY KEY ("Id");


--
-- Name: Sites PK_Sites; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Sites"
    ADD CONSTRAINT "PK_Sites" PRIMARY KEY ("Id");


--
-- Name: Tenants PK_Tenants; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Tenants"
    ADD CONSTRAINT "PK_Tenants" PRIMARY KEY ("Id");


--
-- Name: Users PK_Users; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Users"
    ADD CONSTRAINT "PK_Users" PRIMARY KEY ("Id");


--
-- Name: PeriodAccessRequests PeriodAccessRequests_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PeriodAccessRequests"
    ADD CONSTRAINT "PeriodAccessRequests_pkey" PRIMARY KEY ("Id");


--
-- Name: IX_Categories_TenantId_Name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Categories_TenantId_Name" ON public."Categories" USING btree ("TenantId", "Name");


--
-- Name: IX_Labourers_SupervisorId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Labourers_SupervisorId" ON public."Labourers" USING btree ("SupervisorId");


--
-- Name: IX_Labourers_TenantId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Labourers_TenantId" ON public."Labourers" USING btree ("TenantId");


--
-- Name: IX_MeasurementBooks_SiteId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_MeasurementBooks_SiteId" ON public."MeasurementBooks" USING btree ("SiteId");


--
-- Name: IX_MeasurementBooks_TenantId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_MeasurementBooks_TenantId" ON public."MeasurementBooks" USING btree ("TenantId");


--
-- Name: IX_PaymentAuditLogs_PaymentRequestId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_PaymentAuditLogs_PaymentRequestId" ON public."PaymentAuditLogs" USING btree ("PaymentRequestId");


--
-- Name: IX_PaymentAuditLogs_TenantId_PaymentRequestId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_PaymentAuditLogs_TenantId_PaymentRequestId" ON public."PaymentAuditLogs" USING btree ("TenantId", "PaymentRequestId");


--
-- Name: IX_PaymentRequests_CategoryId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_PaymentRequests_CategoryId" ON public."PaymentRequests" USING btree ("CategoryId");


--
-- Name: IX_PaymentRequests_LabourerId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_PaymentRequests_LabourerId" ON public."PaymentRequests" USING btree ("LabourerId");


--
-- Name: IX_PaymentRequests_SiteId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_PaymentRequests_SiteId" ON public."PaymentRequests" USING btree ("SiteId");


--
-- Name: IX_PaymentRequests_TenantId_ExpenseYear; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_PaymentRequests_TenantId_ExpenseYear" ON public."PaymentRequests" USING btree ("TenantId", "ExpenseYear");


--
-- Name: IX_PaymentRequests_TenantId_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_PaymentRequests_TenantId_Status" ON public."PaymentRequests" USING btree ("TenantId", "Status");


--
-- Name: IX_PaymentRequests_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_PaymentRequests_UserId" ON public."PaymentRequests" USING btree ("UserId");


--
-- Name: IX_Sites_TenantId_Name; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Sites_TenantId_Name" ON public."Sites" USING btree ("TenantId", "Name");


--
-- Name: IX_Users_TenantId_Email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Users_TenantId_Email" ON public."Users" USING btree ("TenantId", "Email") WHERE ("Email" IS NOT NULL);


--
-- Name: IX_Users_TenantId_SixDigitUserId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Users_TenantId_SixDigitUserId" ON public."Users" USING btree ("TenantId", "SixDigitUserId");


--
-- Name: Categories FK_Categories_Tenants_TenantId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Categories"
    ADD CONSTRAINT "FK_Categories_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- Name: Labourers FK_Labourers_Tenants_TenantId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Labourers"
    ADD CONSTRAINT "FK_Labourers_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- Name: Labourers FK_Labourers_Users_SupervisorId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Labourers"
    ADD CONSTRAINT "FK_Labourers_Users_SupervisorId" FOREIGN KEY ("SupervisorId") REFERENCES public."Users"("Id") ON DELETE RESTRICT;


--
-- Name: MeasurementBooks FK_MeasurementBooks_Sites_SiteId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."MeasurementBooks"
    ADD CONSTRAINT "FK_MeasurementBooks_Sites_SiteId" FOREIGN KEY ("SiteId") REFERENCES public."Sites"("Id") ON DELETE CASCADE;


--
-- Name: MeasurementBooks FK_MeasurementBooks_Tenants_TenantId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."MeasurementBooks"
    ADD CONSTRAINT "FK_MeasurementBooks_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- Name: PaymentAuditLogs FK_PaymentAuditLogs_PaymentRequests_PaymentRequestId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PaymentAuditLogs"
    ADD CONSTRAINT "FK_PaymentAuditLogs_PaymentRequests_PaymentRequestId" FOREIGN KEY ("PaymentRequestId") REFERENCES public."PaymentRequests"("Id") ON DELETE CASCADE;


--
-- Name: PaymentRequests FK_PaymentRequests_Categories_CategoryId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PaymentRequests"
    ADD CONSTRAINT "FK_PaymentRequests_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES public."Categories"("Id") ON DELETE SET NULL;


--
-- Name: PaymentRequests FK_PaymentRequests_Labourers_LabourerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PaymentRequests"
    ADD CONSTRAINT "FK_PaymentRequests_Labourers_LabourerId" FOREIGN KEY ("LabourerId") REFERENCES public."Labourers"("Id") ON DELETE SET NULL;


--
-- Name: PaymentRequests FK_PaymentRequests_Sites_SiteId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PaymentRequests"
    ADD CONSTRAINT "FK_PaymentRequests_Sites_SiteId" FOREIGN KEY ("SiteId") REFERENCES public."Sites"("Id") ON DELETE SET NULL;


--
-- Name: PaymentRequests FK_PaymentRequests_Tenants_TenantId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PaymentRequests"
    ADD CONSTRAINT "FK_PaymentRequests_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- Name: PaymentRequests FK_PaymentRequests_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PaymentRequests"
    ADD CONSTRAINT "FK_PaymentRequests_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE RESTRICT;


--
-- Name: PeriodAccessRequests FK_PeriodAccessRequests_RequestedByUser; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PeriodAccessRequests"
    ADD CONSTRAINT "FK_PeriodAccessRequests_RequestedByUser" FOREIGN KEY ("RequestedByUserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: PeriodAccessRequests FK_PeriodAccessRequests_ReviewedByUser; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PeriodAccessRequests"
    ADD CONSTRAINT "FK_PeriodAccessRequests_ReviewedByUser" FOREIGN KEY ("ReviewedByUserId") REFERENCES public."Users"("Id") ON DELETE SET NULL;


--
-- Name: PeriodAccessRequests FK_PeriodAccessRequests_Tenants; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PeriodAccessRequests"
    ADD CONSTRAINT "FK_PeriodAccessRequests_Tenants" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- Name: Sites FK_Sites_Tenants_TenantId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Sites"
    ADD CONSTRAINT "FK_Sites_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- Name: Users FK_Users_Tenants_TenantId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Users"
    ADD CONSTRAINT "FK_Users_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- Name: Labourers Labourers_SiteId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Labourers"
    ADD CONSTRAINT "Labourers_SiteId_fkey" FOREIGN KEY ("SiteId") REFERENCES public."Sites"("Id") ON DELETE SET NULL;


--
-- PostgreSQL database dump complete
--

\unrestrict V3wSkkBU26v3p7fshxSIyP9IS9z8bbDIKaTPgmavvc1cpcksi8HgaagaZEHuXOS

