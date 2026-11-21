# Sourcing & Tender Management System (STMS)

From internal purchase request to final supplier award.  
A centralized B2B procurement and tender management platform built for manufacturers and large organizations.

Explore · Roles · Workflows · Architecture · Setup · Features

---

## ✨ Built around the procurement journey

STMS connects internal department purchase requisitions, supplier bidding, Ollama-powered AI bid analysis, multi-committee evaluations, and executive awarding in a secure, role-based ASP.NET Core MVC portal. Each stakeholder gets a dedicated dashboard and tailored permissions within a unified workspace.

* **Requisitions** → Internal purchase requests with itemized specs, priorities, and approval pipelines.
* **Tendering** → Official tenders, supplier invitations, and strict competitor data isolation.
* **AI Evaluation** → Ollama-powered bid analysis extracting prices, specs, and recommending the best offer privately.
* **Awarding** → Multi-committee scoring, management reviews, QR codes, watermarking, and final contract closure.

> **Security & Privacy Note:** Because the AI evaluation engine runs locally via **Ollama**, sensitive company pricing, tenders, and supplier contracts remain 100% private on internal servers without leaking data to external cloud APIs.

---

## 👥 Six roles, one coordinated workspace

| Role | Primary responsibility | Key tools |
| :--- | :--- | :--- |
| **🏢 Requesting Department** | Submit and track internal purchase needs. | Itemized requisition builder, priority flags, status tracking, department history. |
| **📋 Sourcing Officer** | Manage tenders and invite verified vendors. | Tender setup, evaluation criteria weights, supplier invitations, compliance checks. |
| **⚖️ Evaluation Committee** | Score offers technically and financially. | 4-member scoring panel, weighted criteria matrix, Ollama AI bid insights, unified report. |
| **👔 Sourcing Manager & Top Committee** | Executive review and final awarding. | Consolidated report review, AI-assisted decision support, final award approval, audit logs. |
| **🤝 Supplier** | Self-service bidding in an isolated portal. | Vendor registration, invitation responses, encrypted financial/technical offer submissions, document downloads. |
| **⚙️ System Admin** | Governance, security, and onboarding. | User/department management, Face Recognition enrollment, supplier verification (`Approved`/`Suspended`). |

---

## 🔄 How the workflows connect

1. **Purchase Requisition & Approval**  
   Internal departments (IT, Production, HR, Maintenance) submit itemized purchase requests with specifications, quantities, and target deadlines for review and approval.
2. **Tender Publishing & Isolated Bidding**  
   Approved requisitions are converted into official tenders. Sourcing Officers set locked evaluation criteria weights and invite verified suppliers, who submit offers in complete isolation from competitors.
3. **Compliance Check & Local AI Analysis**  
   Officers verify mandatory legal documents (`Compliant`, `Non-Compliant`, or `Pending Clarification`). **Ollama AI** analyzes bids, highlights strengths/weaknesses, and flags missing files.
4. **Multi-Committee Scoring & Final Awarding**  
   A 4-member committee scores offers to generate a unified report. The Sourcing Manager and Top Committee review consolidated insights, award the winner, and trigger notifications, watermarks, and audit logs.

---

## 🏗️ Application architecture

| Layer / Component | Technology & Specification |
| :--- | :--- |
| **Web application** | C#, ASP.NET Core MVC, ASP.NET Core Identity (Role-Based Access Control). |
| **Database & ORM** | Microsoft SQL Server, Entity Framework Core (Code-First Migrations). |
| **Interface** | HTML5, CSS3, JavaScript (ES6+), Bootstrap 5 with responsive layouts. |
| **Local AI Engine** | Ollama (Local LLM integration for offline/private bid evaluation). |
| **Security & Biometrics** | Live Face Recognition login, Web Speech API (`SpeechRecognition`) voice search, dynamic PDF watermarking, QR codes. |
| **Files & reports** | Server-generated PDF and Excel reports for requisitions, tenders, compliance, and audit trails. |

---

## 🚀 Run locally

### Prerequisites
* **OS / IDE:** Windows + Visual Studio with ASP.NET and web development workload.
* **Runtime:** .NET SDK (8.0 or later).
* **Database:** SQL Server & SQL Server Management Studio (SSMS).
* **AI Service:** [Ollama](https://ollama.com/) installed and running locally (e.g., `ollama run llama3`).

### Setup steps
1. **Clone the repository:**
   ```bash
   git clone [https://github.com/amneh-01/sourcing-tender-management-system.git](https://github.com/amneh-01/sourcing-tender-management-system.git)
   cd sourcing-tender-management-system


Trello :https://trello.com/invite/b/6afaaf7d92cc13dfa0efbaf5/ATTI3ea421f514efa759122987b0fbb4c36d9FC4CF2F/stms-project
Figma :https://www.figma.com/design/69T7gMJl1zdPzUxWz17UyU/STMS?node-id=36-32&p=f&t=brsvmSKpAOg8bnW3-0
