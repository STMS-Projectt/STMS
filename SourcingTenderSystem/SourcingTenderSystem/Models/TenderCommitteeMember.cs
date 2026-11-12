using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("TenderCommitteeMembers")]
public class TenderCommitteeMember
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public int EmployeeProfileId { get; set; }
    public string CommitteeType { get; set; } = string.Empty;
    public string MemberRole { get; set; } = string.Empty;

    public Tender Tender { get; set; } = null!;
    public EmployeeProfile EmployeeProfile { get; set; } = null!;
}
