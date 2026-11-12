using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("EvaluationCriteria")]
public class EvaluationCriterion
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal WeightPercent { get; set; }
    public int MaxScore { get; set; }

    public Tender Tender { get; set; } = null!;
    public ICollection<TechnicalEvaluation> TechnicalEvaluations { get; set; } = new List<TechnicalEvaluation>();
    public ICollection<FinancialEvaluation> FinancialEvaluations { get; set; } = new List<FinancialEvaluation>();
}
