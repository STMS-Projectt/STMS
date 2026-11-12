using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("TechnicalEvaluations")]
public class TechnicalEvaluation
{
    public int Id { get; set; }
    public int BidId { get; set; }
    public int EvaluationCriteriaId { get; set; }
    public int EvaluatorEmployeeProfileId { get; set; }
    public decimal Score { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public Bid Bid { get; set; } = null!;
    public EvaluationCriterion EvaluationCriterion { get; set; } = null!;
    public EmployeeProfile EvaluatorEmployeeProfile { get; set; } = null!;
}
