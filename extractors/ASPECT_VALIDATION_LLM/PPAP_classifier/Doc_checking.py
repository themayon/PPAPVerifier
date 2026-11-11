DOC_TYPE_FIELD_HINTS = {

    "PSW": """
Part Submission Warrant (PSW) / PPA Cover Sheet key identifiers:
- Document title contains: "Part Submission Warrant", "PSW", OR "Cover Sheet PPA Report"
- Sections: Reason for submission (checkboxes)
- Fields: Part Name, Customer Part Number, Drawing Number
- Supplier Name and Address
- Customer Name
- Submission level / PPA level
- Warrant / PPA declaration text
- Customer approval section
- "Customer decision: Ready for series production" or similar approval status
- Authorized Signature and Date
- Print Name, Phone, Title, Email
- Change reason (new part, engineering change, process change, etc.)
- Statement like:
  - "These results meet all drawing and specification requirements"
  - "The PPA procedure was carried out in accordance..."
  - "Ready for series production"
  - "Approval"

IMPORTANT:
"COVER SHEET PPA REPORT" = PSW document type

EXCLUSION HINTS — if ANY of these are present, this page is NOT PSW:
- SPC control charts (UCL/LCL) → IPS page
- Checking aid / Calibration table → CAID page
- Bubble Number + Measurement columns → DIMR page
- Reaction Plan column → Control Plan page
- Failure Mode / RPN columns → PFMEA / DFMEA page
- Operation Number + Operation Description → PFD / Control Plan page
""",

    "MPTR": """
MPTR / MATERIAL TEST REPORT / MATERIAL TEST RESULTS IDENTIFIERS:
- Detect a "Material Test Report" / "Test Report" document in ANY language
STRONG INDICATORS (at least ONE must appear):

- "Material Test Report"
- "Material Test Results"
- "Test Report"          (in any language)
- "Test Results"
- "Production Part Approval - Material Test Results"
- "Werkstoffprüfung"
- "MPTR"
- "Test Results (Material)"
- "Materialprüfung"

THIRD-PARTY LAB TEST REPORTS ARE MPTR:
A test report ISSUED BY a testing laboratory - identifiable by a lab name/logo at
the top, a report number, the sample or part being tested, sample received /
testing dates, and test data - is an MPTR document. A lab logo, letterhead, or an
ISO 17025 mention does NOT make it QLD. QLD is ONLY the lab's accreditation
certificate itself, never a report of test results.

COVER / CONTINUATION PAGES:
The first page of a multi-page test report often carries the "Test Report" title,
sample information and dates but no data table yet (e.g. it is marked "Page 1 of
N") - it is STILL MPTR. The numeric test tables on the following pages continue
the same MPTR document. Do not require the data table to sit on the same page as
the title.

TABLE STRUCTURE INDICATORS (supporting but not mandatory):
STANDARD INDICATORS (SUPPORTING ONLY):

- ISO, EN, DIN, ASTM, JIS, GB standards may appear
- "chemical analysis"
- "mechanical properties"
- "texture examination"
- "Hardness class"
- "Core hardness"
- "Test on δ-Ferrit"
- "Structure"
- "Carbonisation"
- Element rows like:
  - C, Mn, P, S, B, Al, Ti

LAYOUT INDICATORS:

- Columns such as:
  - "Specification / Limits"
  - "Tested"
  - "Results (Data)"
  - "OK / NOT OK"
- Multiple part sections like:
  - Wheel Bolt
  - Washer
  - Scheibe

RULE:
Classify as MPTR if:
- AT LEAST ONE STRONG INDICATOR is present
AND
- EITHER the page contains measurable test data (numeric or descriptive results)
  OR it is the title/cover page of a multi-page test report whose data pages follow.

EXCLUSION HINTS (DO NOT CLASSIFY AS MPTR IF THESE DOMINATE):

- "Cover sheet PPA report" → PPA Cover Sheet
- SPC charts / control charts → IPS
- "Reaction Plan" column → Control Plan (CP)
- "Failure Mode", "RPN" → PFMEA
- "Measurement 1 / 2 / 3" columns → DIMR
- Flow diagrams / arrows → PFD
- Pure compliance declarations (IMDS, BOMcheck) without test tables → NOT MPTR
- An accreditation CERTIFICATE / scope-of-accreditation document (not a test
  report) → QLD
""",

    "MSA": """
Measurement System Analysis (MSA) / Gauge R&R Study key identifiers:
- Gage Name / Gauge Name field (e.g., "Blade Micrometer 817a")
- Gage Number / Gauge Number field
- Characteristic being measured
- Number of Parts, Number of Repetitions, Number of Inspectors / Operators fields
- %GRR or %Study Var column (Total Gage R&R row)
- NDC (Number of Distinct Categories) field
- Inspector / Appraiser result tables (Inspector A, Inspector B, Inspector C)
- Between Inspectors / All Inspectors versus Std columns (Attribute MSA)
- Disagreement OK/NOK row per inspector
- Kappa value field
- Upper Limit / Lower Limit / Tolerance / Specification fields
- "GAGE R&R List" or "MSA Report" or "MSA Index" summary tables

EXCLUSION HINTS — if ANY of these are present, this page is NOT MSA:
- Pp / Ppk / Cp / Cpk capability values + SPC charts → this is an IPS page
- Bubble Number + Measurement 1, 2, 3... individual value columns → this is a DIMR page
- Reaction Plan column → this is a CP page
- Failure Mode / RPN column → this is a PFMEA page
- Operation Number + Operation Description → this is a PFD or CP page
""",

    "CAID": """
Checking Aid / Inspection Device Document (CAID) key identifiers:
- Inspection tool / instrument / gauge list, OR a calibration document for a tool
- Calibration certificates are CAID: "校准证书" / "CALIBRATION CERTIFICATE" / "Certificate No."
- Instrument verification reports are CAID: "仪器校验报告" / "Calibration Report"
- Instrument details: Instrument Name, Instrument No. (e.g., MY-J1QA-xxx), Model, Manufacturer
- Calibration date, due date / valid until, calibration results, Pass/Fail or 合格
- Tables like Nominal/Standard Value, Measured Value, Deviation (基准值 / 实测值 / 偏差值)

CONTINUATION PAGES:
- A CAID document can run over multiple continuous pages
- Pages without a header or title that continue the previous page's calibration table,
  results, or notes are still CAID (e.g., a page with only a 基准值 / 实测值 / 偏差值 table,
  or only uncertainty notes / "以下空白")

EXCLUSION HINTS — if ANY of these are present, this page is NOT CAID:
- SPC control charts (UCL/LCL) → IPS page
- Bubble Number + Measurement 1, 2, 3... columns → DIMR page
- Reaction Plan column → CP page
- Failure Mode / RPN → PFMEA page
- SAMPLE INSPECTION DATA REPORT → not CAID
- %GRR / NDC / Appraiser tables → MSA page
""",

    "QLD": """
Qualified Laboratory Documentation (QLD) key identifiers:
QLD is ONLY the laboratory's ACCREDITATION / QUALIFICATION document - proof that
the lab is qualified to perform tests. It is NOT a report of test results.

STRONG INDICATORS (the page must be about the lab's qualification itself):
- An accreditation CERTIFICATE (e.g. ISO/IEC 17025)
- A "Scope of Accreditation" document listing the tests the lab is accredited for
- An accreditation body's certificate, seal, or logo issued TO the laboratory
- Accreditation certificate number with issue / validity dates
- A statement that the laboratory is accredited or qualified for specific methods

CRITICAL DISAMBIGUATION (QLD vs MPTR):
- A test report ISSUED BY a lab - with sample information, a report number,
  testing dates, and test results - is MPTR, NOT QLD, even when the lab logo or
  letterhead is prominent and even when it mentions ISO 17025.
- QLD is ONLY the accreditation certificate / scope document itself.
- If the page shows any test results or sample test data → it is MPTR.
- A lab logo or letterhead ALONE does not make a page QLD.

EXCLUSION HINTS — if ANY of these are present, this page is NOT QLD:
- "Test Report" / "Test Results" title with sample data → MPTR
- A test results table or measurement / sample data → MPTR
- SPC control charts → IPS
- Bubble Number + Measurement columns → DIMR
- Reaction Plan column → CP
- Failure Mode / RPN → PFMEA
- %GRR / NDC / Inspector tables → MSA
""",

    "IPS": """
Initial Process Study / Cpk Study (IPS) key identifiers:
- Document title: "Initial Process Study", "IPS", "Process Capability Study", or "Cpk Study"
- Header fields: Part Name, Part Number, Revision Number, Number of Samples
- Characteristic column with bubble numbers (simple numeric: 1, 2, 3)
- Special Characteristics column (symbols: DR1, DR2, ⊕, ∆)
- Nominal / USL / LSL columns
- Pp / Ppk columns under "Overall" capability section
- Cp / Cpk columns under "Within" capability section
- Subgroups count field
- SPC control charts: X-Bar, Individuals, Moving Range, Range, R Chart, P Chart, U Chart, C Chart, NP Chart
- UCL / CL / LCL control limit lines with plotted data points
- Normal Probability Plot with "AD:" and "P:" values
- NELSON rules result block
- XBAR Checking Limits / R Checking Limits fields
- Process capability results table

EXCLUSION HINTS — if ANY of these are present, this page is NOT IPS:
- Reaction Plan column → this is a Control Plan (CP) page
- Failure Mode or RPN column → this is a PFMEA or DFMEA page
- Measurement Method column + individual Measurement 1, 2, 3... value columns together → this is a DIMR page
- Acceptance / Pass / Fail column alongside individual measurement rows → this is a DIMR page
- Bubble Number paired with Dimension Type column → this is a DIMR page
- Number of Measurements column → this is a DIMR page
- Sample Size + Sample Frequency + Control Method columns together → this is a CP page
- Gauge R&R / %GRR / NDC / Number of Inspectors / Number of Repetitions fields → this is an MSA page
- Between Inspector or All Inspectors versus Std columns → this is an MSA page
- Flow chart symbols (rectangles, diamonds, arrows) → this is a PFD page
""",

    "DIMR": """
Dimensional Measurement Report (DIMR) key identifiers:
- Bubble Number column AND Dimension Type column present together
- Nominal / Upper Limit / Lower Limit / Tolerance columns
- Special Characteristics column (⊕, ∆, CL2, CL3)
- Measurement Method column (e.g., "CMM", "Micrometer", "Caliper")
- Number of Measurements column
- Individual measurement value columns: Measurement 1, Measurement 2, Measurement 3, Measurement 4, Measurement 5
- Acceptance / OK / Not Ok column per feature row
- Organization / Supplier / Vendor Code in document header
- Inspector field in header
- Lot / Date / Material / Sampling Reason fields in header
- Total Bubbles count field
- Each row = ONE dimensional feature with its own spec limits and measured values

ALSO MATCH these supporting pages (they belong to the DIMR package):
- Page titled "Certificate of Conformance" / "Certificate of Compliance" / "C of C" / "COC".
  A CoC that lists material, heat treat, passivation or finish lines is STILL a CoC → match.
- Raw-material certificate from the mill/material supplier with a Heat number and
  chemical and/or mechanical test results ("Certificate of Test", "Mill Test Report",
  "MTR", "Material Certificate", "EN 10204 3.1"), including its continuation pages
  (e.g. page 2 with the chemical composition table).

DO NOT MATCH:
- REACH / RoHS / WEEE / Prop 65 compliance letters
- Plating / coating / passivation / heat-treat process certifications
- Packing List / Packing Slip

EXCLUSION HINTS — if ANY of these are present, this page is NOT DIMR:
- SPC control charts (X-Bar, R Chart, P Chart) with UCL / CL / LCL plotted → this is an IPS page
- Pp / Ppk / Cp / Cpk capability index values → this is an IPS page
- Normal Probability Plot with "AD:" and "P:" values → this is an IPS page
- NELSON rules result block → this is an IPS page
- Subgroups count field → this is an IPS page
- XBAR Checking Limits or R Checking Limits → this is an IPS page
- Reaction Plan column → this is a Control Plan (CP) page
- Failure Mode or RPN column → this is a PFMEA or DFMEA page
- Flow chart symbols (rectangles, diamonds, arrows) → this is a PFD page
- Sample Size + Sample Frequency + Control Method columns together → this is a CP page
- Gauge R&R / %GRR / NDC / Number of Inspectors fields → this is an MSA page
- Operation Number + Operation Description columns → this is a PFD or CP page
""",

    "PFD": """
Process Flow Diagram (PFD) key identifiers:

Classify the current page as PFD if it shows a process flow layout with:
- STEP numbers
- Flow-symbol columns such as FABRICATION, MOVE, STORE, INSPECT
- Flow symbols or flow lines, such as diamond, circle, triangle, square, arrows
- Process / Operation description
- Characteristics, specification/tolerance, control method, or reaction plan

IMPORTANT:
- Do NOT require the title "PROCESS FLOW DIAGRAM".
- Do NOT require Part Number, Prepared By, or PFD Date.
- Classify using ONLY the current page content.

PFD vs CP difference:
- If the page has FABRICATION / MOVE / STORE / INSPECT flow-symbol columns, classify as PFD.
- If the page has MACHINE / DEVICE / JIG / TOOLS FOR MFG column and no flow-symbol columns, classify as CP.

EXCLUSION HINTS — if ANY of these are present, this page is NOT PFD:
- MACHINE / DEVICE / JIG / TOOLS FOR MFG without flow-symbol columns → CP page
- Failure Mode, Effect, Cause, RPN, AP → PFMEA page
- Measurement 1, Measurement 2, Measurement 3 result columns → DIMR page
- UCL/LCL control charts → IPS page
- %GRR, NDC, Inspector, Trial tables → MSA page
""",

    "CP": """
Control Plan (CP) key identifiers:
- Document title: "CONTROL PLAN" printed clearly at top
- Reaction Plan column (unique to CP — not found in any other doc type)
- Sample Frequency and Sample Size columns
- Gauge Number / Evaluation Measurement Technique columns
- Upper Limit / Lower Limit / Tolerance / Specification columns
- Control Method column
- Operation Number and Operation Description (Process Name) columns
- Special Characteristics symbol column (Class / Special Char. Class)
- Machine / Device / Jig / Tool for MFG column
- Part / Process Number, Prototype / Pre-Launch / Production checkboxes in header
- Control Plan Number, Supplier Code, Supplier Plant fields
RULE:
- If "MACHINE / DEVICE / JIG / TOOLS FOR MFG" column is NOT present, the page is NOT a Control Plan.
- The page contains control plan structure: process + characteristic + spec + method + frequency + record + reaction/disposal

EXCLUSION HINTS — if ANY of these are present, this page is NOT CP:
- SPC control charts (UCL/LCL) → this is an IPS page
- Measurement 1, 2, 3... individual value columns → this is a DIMR page
- Failure Mode / RPN columns → this is a PFMEA page
- %GRR / NDC / Inspector tables → this is an MSA page
""",

    "PFMEA": """
Process FMEA (PFMEA) key identifiers:
- Document title: "PFMEA" or "Process Failure Mode and Effects Analysis"
- Function / Process Step column
- Potential Failure Mode column (e.g., "oversized", "undersized", "flash", "non-fill")
- Potential Effect of Failure column with Severity (SEV) rating (1-10)
- Potential Cause of Failure column with Occurrence (OCC) rating (1-10)
- Current Process Controls Prevention column
- Detection Controls column with Detection (DET) rating (1-10)
- RPN (Risk Priority Number) column — SEV × OCC × DET
- Recommended Action / Responsibility / Target Date / Actions Taken columns
- New SEV / New OCC / New DET / New RPN columns (post-action ratings)
- FMEA # field in header
- Core Team, Process Responsibility, PFMEA Date fields

EXCLUSION HINTS — if ANY of these are present, this page is NOT PFMEA:
- Reaction Plan column → this is a CP page
- SPC control charts → this is an IPS page
- Measurement 1, 2, 3... individual value columns → this is a DIMR page
- %GRR / NDC / Inspector tables → this is an MSA page
- Function / Design Requirement column (design-level) → this is a DFMEA page
""",

    "IMDS": """
IMDS / MDS document key identifiers:
IMDS is a MATERIAL DATA DECLARATION submitted into the International Material
Data System - a substance/material composition breakdown of a part for
compliance tracking. It is NOT a laboratory test report.

STRONG INDICATORS (at least ONE must appear):
- "IMDS ID"
- "MDS status"
- "MDS Report"
- "Recipient Company"
- "Recipient Status"
- "Substances of assemblies and materials"
- "International Material Data System"
- "BOM Check

CRITICAL DISAMBIGUATION (IMDS vs MPTR):
- A laboratory TEST REPORT with chemical/material test result tables (tested
  values vs specification limits, hardness, element analysis) is MPTR, NOT IMDS.
- IMDS is a declaration of material/substance composition in the IMDS/MDS system,
  identified by the IMDS-specific fields above - not by the presence of chemical
  data alone.
- If the page is a test report showing measured results, it is MPTR.

RULE:
Classify as IMDS ONLY if one of the STRONG INDICATORS above is present.

EXCLUSION HINTS:
- "Cover sheet PPA report" → PPA Cover Sheet
- "Test Report" / "Material Test Results" with measured test data → MPTR
- SPC charts → IPS
- Reaction Plan column → CP
- Failure Mode / RPN → PFMEA
- Measurement 1,2,3 columns → DIMR
- Flowchart arrows → PFD
""",

    "APDS": """
APDS document key identifiers:
STRONG INDICATOR (MANDATORY):
- Document title containing "APDS" or Approval or dimensional study data

RULE:
Classify as APDS ONLY if the title "APDS"

EXCLUSION HINTS — if ANY of these are present, this page is NOT APDS:
- SPC control charts → this is an IPS page
- Reaction Plan column → this is a CP page
- Failure Mode / RPN → this is a PFMEA page
""",

    "AAR": """
Appearance Approval Report (AAR) key identifiers:

STRONG INDICATORS:
- Title: "Appearance Approval Report"
- "Appearance Evaluation" section
- "Color Evaluation" table
- Color properties like Hue, Value, Chroma, Gloss
- Customer or Authorized Representative signature

RULE:
If the page focuses on color or appearance evaluation, classify as AAR.

EXCLUSION HINTS:
- SPC charts → IPS
- Reaction Plan column → CP
- Failure Mode / RPN → PFMEA
- Measurement 1,2,3 columns → DIMR
- %GRR / Inspector tables → MSA
- Flowchart arrows / diamonds → PFD
""",

    "ECD": """
Engineering Change Document (ECD) key identifiers:
MANDATORY INDICATOR:
- Title containing "Engineering Change"
SUPPORTING INDICATORS:
- Change description
- Change reason
- Before / After comparison
- Change level or effective date
RULE:
Classify as ECD ONLY if "Engineering Change" appears in the document.

EXCLUSION HINTS:
- SPC charts → IPS
- Reaction Plan → CP
- Failure Mode / RPN → PFMEA
""",

    "CEA": """
Customer Engineering Approval (CEA) key identifiers:
MANDATORY INDICATOR:
- Title containing "Customer Engineering Approval"
SUPPORTING INDICATORS:
- Customer approval signature
- Approval date
RULE:
Classify as CEA ONLY if "Customer Engineering Approval" appears in the document.
EXCLUSION HINTS:
- SPC charts → IPS
- Reaction Plan → CP
- Failure Mode / RPN → PFMEA
""",

    "DFMEA": """
Design FMEA (DFMEA) key identifiers:
- Document title: "DFMEA" or "Design Failure Mode and Effects Analysis"
- Function / Design Requirement column (design-level, not process-level)
- Potential Failure Mode column
- Potential Effect of Failure and Severity rating
- Potential Cause of Failure and Occurrence rating
- Detection controls and Detection rating
- RPN column
- Focus on design functions, NOT manufacturing process steps

EXCLUSION HINTS — if ANY of these are present, this page is NOT DFMEA:
- Reaction Plan column → this is a CP page
- SPC control charts → this is an IPS page
- Measurement 1, 2, 3... columns → this is a DIMR page
- Process step descriptions like "Receive resin", "Store", "Lathe" → this is a PFMEA page
""",

    "SAMP": """
Samples document (SAMP) key identifiers:

MANDATORY INDICATOR:
- Title containing "Sample" or "Sample Submission"

SUPPORTING INDICATORS:
- Sample identification
- Lot number
- Sample submission details

RULE:
Classify as SAMP ONLY if "Sample" appears as a document title.

EXCLUSION HINTS:
- SPC charts → IPS
- Reaction Plan → CP
- Failure Mode / RPN → PFMEA
""",

    "CSR": """
Customer Specific Requirements (CSR) key identifiers:

MANDATORY INDICATOR:
- Title containing "Customer Specific Requirements"

SUPPORTING INDICATORS:
- Requirement clauses
- Reference to IATF 16949 or customer manuals

RULE:
Classify as CSR ONLY if "Customer Specific Requirements" appears.

EXCLUSION HINTS:
- SPC charts → IPS
- Reaction Plan → CP
- Failure Mode / RPN → PFMEA
""",

    "MSAM": """
MSA Method document (MSAM) key identifiers:

MANDATORY INDICATOR:
- Title MUST contain "MSA Method" or "Measurement System Analysis Method"

RULE:
Classify as MSAM ONLY if the document describes a METHOD or PROCEDURE,
NOT analysis results.

EXCLUSION HINTS — if ANY appear, this is NOT MSAM:
- Title "Measurement System Analysis" (without "Method") → MSA
- Charts or graphs → MSA
- Measurement data tables → MSA or DIMR
- %GRR / NDC / statistics → MSA
- Cp / Cpk → IPS
- SPC charts → IPS
- Reaction Plan → CP
"""
}