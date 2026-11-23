using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SourcingTenderSystem.ViewModels;

// Filters shared by every report tab and by the PDF / Excel downloads. Bound from the query string.
public class ReportFilter
{
    public string Tab { get; set; } = "requests";

    [DataType(DataType.Date)]
    public DateOnly? From { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? To { get; set; }

    public int? DepartmentId { get; set; }
    public int? SupplierId { get; set; }
    public string? TenderStatus { get; set; }

    // Only used on the "Bids & evaluation" tab: show the bids of one tender.
    public int? TenderId { get; set; }

    // Dates are stored in UTC: the "from" day starts at 00:00 and the "to" day ends at 23:59:59.
    public DateTime? FromUtc
    {
        get
        {
            if (From == null) return null;
            return From.Value.ToDateTime(TimeOnly.MinValue);
        }
    }

    public DateTime? ToUtc
    {
        get
        {
            if (To == null) return null;
            return To.Value.ToDateTime(TimeOnly.MaxValue);
        }
    }
}

// One headline number (KPI). Link opens the details behind the number.
public class ReportKpi
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Hint { get; set; }
    public string Icon { get; set; } = "chart";
    public string Tone { get; set; } = "blue";
    public string? Link { get; set; }
}

// One table of a report. The same table is shown on the page and written to Excel and PDF.
public class ReportTable
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Note { get; set; }
    public string[] Headers { get; set; } = new string[0];
    public List<object?[]> Rows { get; set; } = new List<object?[]>();
    public List<string?> Links { get; set; } = new List<string?>();
    public bool LinksOpenDialog { get; set; }
    public string Empty { get; set; } = "";

    // Adds one row. The link (can be null) is put on the first cell of the row.
    public void Add(string? link, object?[] cells)
    {
        Rows.Add(cells);
        Links.Add(link);
    }
}

public class ReportSection
{
    public string Tab { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ReportKpi> Kpis { get; set; } = new List<ReportKpi>();
    public List<ReportTable> Tables { get; set; } = new List<ReportTable>();
    // Optional free-text points printed after the tables in the PDF (for example the AI recommendations).
    public string? NotesTitle { get; set; }
    public List<string> Notes { get; set; } = new List<string>();
}

public class ReportsPage
{
    public ReportFilter Filter { get; set; } = new ReportFilter();
    public ReportSection Section { get; set; } = new ReportSection();
    public List<SelectListItem> Departments { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> Suppliers { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> Tenders { get; set; } = new List<SelectListItem>();
    public List<string> TenderStatuses { get; set; } = new List<string>();
    public string? FilterError { get; set; }
}

// One bar of a dashboard chart (a department, a status or a supplier).
public class DashboardBar
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Count { get; set; }
}
