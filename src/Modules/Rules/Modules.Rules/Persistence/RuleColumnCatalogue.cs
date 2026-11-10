using DES.Modules.Rules.Contracts;

namespace DES.Modules.Rules.Persistence;

internal sealed record RuleColumnSeed(string Name, string Key, RuleColumnKind Kind, RuleColumnDataType DataType, string? HeaderNames = null);

internal static class RuleColumnCatalogue
{
    private const RuleColumnKind Column = RuleColumnKind.Column;
    private const RuleColumnKind Header = RuleColumnKind.Header;

    internal static IReadOnlyList<RuleColumnSeed> Defaults { get; } =
    [
        new("Item / Function", "itemFunction", Column, RuleColumnDataType.Text, "Item, Function, Process Step, Process Function, Requirements"),
        new("Failure Mode", "failureMode", Column, RuleColumnDataType.Text, "Potential Failure Mode, Failure Mode"),
        new("Effect of Failure", "failureEffect", Column, RuleColumnDataType.Text, "Potential Effect(s) of Failure, Effect"),
        new("Severity", "severity", Column, RuleColumnDataType.Number, "Severity, SEV, S"),
        new("Classification", "classification", Column, RuleColumnDataType.Text, "Class, Classification, Special Characteristic, SC/CC"),
        new("Cause of Failure", "failureCause", Column, RuleColumnDataType.Text, "Potential Cause(s) of Failure, Cause"),
        new("Prevention Controls", "preventionControls", Column, RuleColumnDataType.Text, "Current Prevention Controls, Prevention"),
        new("Occurrence", "occurrence", Column, RuleColumnDataType.Number, "Occurrence, OCC, O"),
        new("Detection Controls", "detectionControls", Column, RuleColumnDataType.Text, "Current Detection Controls, Detection Control"),
        new("Detection", "detection", Column, RuleColumnDataType.Number, "Detection, DET, D"),
        new("RPN", "rpn", Column, RuleColumnDataType.Number, "RPN, Risk Priority Number"),
        new("Action Priority", "actionPriority", Column, RuleColumnDataType.Text, "AP, Action Priority"),
        new("Recommended Action", "recommendedAction", Column, RuleColumnDataType.Text, "Recommended Action(s), Recommended Actions"),
        new("Responsibility", "responsibility", Column, RuleColumnDataType.Text, "Responsibility, Owner, Responsible"),
        new("Target Date", "targetDate", Column, RuleColumnDataType.Date, "Target Date, Target Completion Date, Due Date"),
        new("Actions Taken", "actionsTaken", Column, RuleColumnDataType.Text, "Actions Taken, Action Taken"),
        new("Completion Date", "completionDate", Column, RuleColumnDataType.Date, "Completion Date, Completed Date, Actual Completion Date"),
        new("New Severity", "newSeverity", Column, RuleColumnDataType.Number, "New SEV, Revised Severity"),
        new("New Occurrence", "newOccurrence", Column, RuleColumnDataType.Number, "New OCC, Revised Occurrence"),
        new("New Detection", "newDetection", Column, RuleColumnDataType.Number, "New DET, Revised Detection"),
        new("New RPN", "newRpn", Column, RuleColumnDataType.Number, "New RPN, Revised RPN"),
        new("Process Number", "processNumber", Column, RuleColumnDataType.Text, "Process Number, Op No, Operation Number"),
        new("Characteristic", "characteristic", Column, RuleColumnDataType.Text, "Characteristic, Product Characteristic, Process Characteristic"),
        new("Specification / Tolerance", "specification", Column, RuleColumnDataType.Text, "Specification, Tolerance, Product/Process Specification/Tolerance"),
        new("Evaluation Method", "evaluationMethod", Column, RuleColumnDataType.Text, "Evaluation/Measurement Technique, Gauge"),
        new("Sample Size", "sampleSize", Column, RuleColumnDataType.Number, "Sample Size, Size"),
        new("Sample Frequency", "sampleFrequency", Column, RuleColumnDataType.Text, "Sample Frequency, Frequency, Freq"),
        new("Control Method", "controlMethod", Column, RuleColumnDataType.Text, "Control Method"),
        new("Reaction Plan", "reactionPlan", Column, RuleColumnDataType.Text, "Reaction Plan"),
        new("Part Number", "partNumber", Header, RuleColumnDataType.Text, "Part Number, Part No, P/N"),
        new("Part Name", "partName", Header, RuleColumnDataType.Text, "Part Name, Description"),
        new("Drawing Revision", "drawingRevision", Header, RuleColumnDataType.Text, "Drawing Revision, Engineering Change Level, Rev"),
        new("Supplier Name", "supplierName", Header, RuleColumnDataType.Text, "Supplier Name, Organization Name, Supplier"),
        new("Supplier Code", "supplierCode", Header, RuleColumnDataType.Text, "Supplier Code, Vendor Code"),
        new("Customer", "customer", Header, RuleColumnDataType.Text, "Customer, Customer Name"),
        new("Document Number", "documentNumber", Header, RuleColumnDataType.Text, "FMEA Number, Control Plan Number, Document No"),
        new("Original Date", "originalDate", Header, RuleColumnDataType.Date, "Original Date, Date (Orig.), Key Date"),
        new("Revision Date", "revisionDate", Header, RuleColumnDataType.Date, "Revision Date, Date (Rev.)"),
        new("Prepared By", "preparedBy", Header, RuleColumnDataType.Text, "Prepared By"),
        new("Approved By", "approvedBy", Header, RuleColumnDataType.Text, "Approved By, Customer Approval"),
        new("Core Team", "coreTeam", Header, RuleColumnDataType.Text, "Core Team, Team"),
    ];
}
