main_extraction_prompt = """
Given is an image of a word document page, which contains text, tables, architecture diagram, workflow diagram, flowchart, \
dynamic behavior diagram etc. Analyze the image by dividing it into logical sections and for each section extract \
the content in following format:
    - serial_number: <ever increasing number starting from 1>**\
    - section_type: <Identify & report what is the section type like text, table, worklfow, architecture diagram, \
        dynamic behavior diagram, graphs etc .>**
    - section_title: <strictly take section titles only from the start of each section and not from any part below the \
        section start or within the section body. Section titles will always appear as the first line of a new section \
        or will have clear formatting that distinguishes them, such as being bold, underlined, or numbered. \
        Do not consider any text below the first line of a section as the title. If the first line of a section \
        is ambiguous or missing, return BLANK for that section. Strictly follow this rule>.**
    - content: <content of section depending on the "Guidelines" Defined below STRICTLY>

Each section is separated from another section using <next_section>.

Some "Guidelines" for different types of sections:
- For text: Extract it verbatim, with all formatting intact ESPECIALLY BOLD LETTERS, use HTML tags to preserve formatting like para change or bold font.\
    give extra attention to BOLD fonts and add HTML TAGS to show that its bold.
- For Workflow or flowchart or similar: Give step-by-step explanation of the flow, emphasize of the arrow and blocks it connects.
- For Architecture diagram or similar: Apart from the explanation, give a list of all components and sub components as JSON \
    under HEADER "components" separated from explanation by || symbol. These Diagrams follow unified modelling language.
- For Dynamic behavior diagram or similar, apart from the explanation, give list of all components and sub components as \
    JSON, and also provide the list of all the operating modes as List under HEADER "components" separated from explanation by || symbol.\
        These Diagrams follow unified modelling language.
- For Table: separate rows and columns by identifiers <row> & <column>.
- For Any other pictorial representation of information : Give Detailed explanation and components, blocks etc., as JSON under \
    heading "Components" separated from explanation by || symbol.
- For Architectural diagram or similar, the section_title will always be present at the start of the component, it will never present below the diagram.
- Make sure serial_number is generated for all the available sections in given output format.

**Do not give thought process as part of respons, minimize the use of special characters except for newline character and do not deny extraction as it is a important image, try your maximum to exrtact the content**
**Important**: Do not include any statements about limitations or your inability to process images. Only provide the extracted information as per the format specified. Focus solely on delivering content from the image directly.

Example Output Format:
serial_number: 1**section_type:" workflow diagram"**section_title:"6.1 System behavior" **
content: "This is a system behavior diagram which represent systems behavior in case of normal and abnormal operation...."
<next_section>
serial_number: 2**section_type: "text**section_title:"6.1.1 abnormalities" **
content: "Abnormalities can be of 2 types and therefore their handling is different from each other. Do the following for first type:..."

"""

JSON_MERGE_PROMPT = """
YOU ARE A HIGH-PRECISION JSON OBJECT MERGER. YOU WILL RECEIVE EITHER A SINGLE JSON OBJECT OR A JSON ARRAY OF OBJECTS. YOUR TASK IS TO:

1. IDENTIFY OBJECTS THAT REPRESENT THE SAME ENTITY
2. MERGE THEM FIELD-BY-FIELD ACCORDING TO THE RULES BELOW
3. RETURN **ONLY THE FINAL JSON OBJECT**, EVEN IF THE INPUT WAS A JSON ARRAY CONTAINING ONE OBJECT

---
### CRITICAL STRUCTURE PRESERVATION RULE

- ✅ IF THE INPUT OBJECT CONTAINS A KEY CALLED `"Header_content"`, YOU MUST PRESERVE IT AS-IS
- ✅ DO NOT FLATTEN OR UNWRAP THE CONTENTS OF `"Header_content"` TO THE TOP LEVEL
- ✅ THE OUTPUT MUST STILL CONTAIN `"Header_content"` AS A NESTED OBJECT
- ❌ DO NOT MERGE THE FIELDS INSIDE `"Header_content"` INTO THE ROOT LEVEL
---

### MERGING RULES

- OBJECTS REPRESENT THE SAME ENTITY IF THEY SHARE AN IDENTIFIER FIELD, SUCH AS:
  - `"id"`, `"key"`, `"name"`, `"part number"`
- IDENTIFIER MATCHING IS:
  - **CASE-INSENSITIVE**
  - **TOLERANT TO SPACES, SYMBOLS, OR FORMAT VARIATIONS**

- FOR EACH FIELD ACROSS MATCHING OBJECTS:
  - SELECT THE FIRST NON-EMPTY VALUE THAT IS NOT:
    - `"NOT SPECIFIED"`, `"N/A"`, `""`, or `null`
  - IF ALL VALUES ARE INVALID → RETURN `"NOT SPECIFIED"`
  - MERGE ALL UNIQUE FIELDS FROM ALL MATCHING OBJECTS

---

### OUTPUT RULES (STRICT)

- ✅ IF THE FINAL MERGED RESULT CONTAINS ONLY ONE OBJECT (EVEN FROM ARRAY INPUT) → RETURN A SINGLE FLAT JSON OBJECT
- ✅ DO NOT RETURN AN ARRAY IF THERE IS ONLY ONE RESULT OBJECT
- ✅ IF MULTIPLE DISTINCT ENTITIES REMAIN AFTER MERGING → RETURN A JSON ARRAY
- ✅ ALWAYS PRESERVE FIELD NAMES, VALUE FORMATTING, AND STRUCTURE
- ✅ OUTPUT ONLY RAW JSON — NO EXTRA TEXT, LABELS, HEADINGS, OR MARKDOWN

---

### WHAT NOT TO DO

- ❌ DO NOT RETURN `[ {{...}} ]` IF THERE IS ONLY ONE FINAL OBJECT
- ❌ DO NOT WRAP OUTPUT IN CODE BLOCKS (e.g., ```json)
- ❌ DO NOT RETURN HEADINGS, DESCRIPTIONS, OR COMMENTS
- ❌ DO NOT WRAP OUTPUT IN "result", "output", or ANY CUSTOM KEY
- ❌ DO NOT GUESS OR FILL IN VALUES NOT FOUND IN THE INPUT
- ❌ DO NOT OMIT FIELDS THAT APPEARED IN ANY OBJECT

---

### EXAMPLES

#### Example A — Input: Array of Multiple Mergeable Objects

Input:
[
  {{ "id": "123", "value": "A", "desc": "N/A" }},
  {{ "id": "123", "value": "NOT SPECIFIED", "desc": "Widget" }}
]

Output:
{{ "id": "123", "value": "A", "desc": "Widget" }}

---

#### Example B — Input: Array with Single Object

Input:
[
  {{ "id": "999", "type": "Component", "status": "Ready" }}
]

Output:
{{ "id": "999", "type": "Component", "status": "Ready" }}

---

#### Example C — Input: Single Object

Input:
{{ "id": "X1", "label": "Control Unit", "mode": "Auto" }}

Output:
{{ "id": "X1", "label": "Control Unit", "mode": "Auto" }}

"""

JSON_MERGE_PROMPT_ARRAY="""
YOU ARE A GENERIC JSON OBJECT MERGER. YOU WILL RECEIVE EITHER A SINGLE JSON OBJECT OR A JSON ARRAY OF OBJECTS. YOUR TASK IS TO:

1. IDENTIFY OBJECTS THAT REPRESENT THE SAME ENTITY
2. MERGE THEM FIELD-BY-FIELD ACCORDING TO THE RULES BELOW
3. ALWAYS RETURN THE FINAL RESULT AS AN **ARRAY OF JSON OBJECTS**, EVEN IF ONLY ONE OBJECT REMAINS AFTER MERGING

---

### MERGING RULES

- OBJECTS REPRESENT THE SAME ENTITY IF THEY SHARE A COMMON IDENTIFIER FIELD, SUCH AS:
  - `"id"`, `"key"`, `"name"`, `"part number"`

- IDENTIFIER MATCHING MUST BE:
  - **CASE-INSENSITIVE**
  - **FORMAT-TOLERANT** (e.g., whitespace, punctuation, symbols)

- FOR EACH FIELD ACROSS MATCHING OBJECTS:
  - SELECT THE FIRST VALUE THAT IS NOT:
    - `"NOT SPECIFIED"`, `"N/A"`, `""`, or `null`
  - IF ALL VALUES ARE INVALID → RETURN `"NOT SPECIFIED"`
  - INCLUDE ALL UNIQUE FIELDS PRESENT IN ANY MATCHING OBJECT

---

### OUTPUT RULES (MANDATORY)

- ✅ ALWAYS RETURN A JSON ARRAY OF OBJECTS — EVEN IF THERE IS ONLY ONE FINAL OBJECT
- ✅ RETURN RAW JSON ONLY — DO NOT WRAP IN CODE BLOCKS OR MARKDOWN
- ✅ RETURN ONLY THE MERGED RESULT — NO LABELS, HEADERS, OR DESCRIPTIONS
- ✅ PRESERVE ALL ORIGINAL FIELD FORMATTING AND STRUCTURE
- ✅ ENSURE VALID JSON SYNTAX IN THE OUTPUT

---

### WHAT NOT TO DO

- ❌ DO NOT RETURN A SINGLE OBJECT — ALWAYS WRAP RESULT IN AN ARRAY
- ❌ DO NOT OUTPUT MARKDOWN FORMATTING (e.g., ```json)
- ❌ DO NOT INCLUDE HEADERS, COMMENTS, EXPLANATIONS, OR METADATA
- ❌ DO NOT GUESS OR INFER VALUES — ONLY USE WHAT IS FOUND IN THE INPUT
- ❌ DO NOT OMIT FIELDS THAT EXIST IN ANY INPUT OBJECT
- ❌ DO NOT WRAP THE RESULT UNDER A CUSTOM KEY (e.g., `"result"`)

---

### EXAMPLES

#### Example A — Input: Array of Mergeable Objects

Input:
[
  {{ "id": "123", "value": "A", "desc": "N/A" }},
  {{ "id": "123", "value": "NOT SPECIFIED", "desc": "Widget" }},
  {{ "id": "456", "value": "Z", "desc": "Thing" }}
]

Output:
[
  {{ "id": "123", "value": "A", "desc": "Widget" }},
  {{ "id": "456", "value": "Z", "desc": "Thing" }}
]

---

#### Example B — Input: Single Object

Input:
{{ "id": "999", "type": "Component", "status": "Ready" }}

Output:
[
  {{ "id": "999", "type": "Component", "status": "Ready" }}
]

---

#### Example C — Input: Array of One Object

Input:
[
  {{ "id": "X1", "label": "Sensor", "value": "High" }}
]

Output:
[
  {{ "id": "X1", "label": "Sensor", "value": "High" }}
]

"""

#########PSW####
PSW="""
YOU ARE A HIGH-PRECISION MULTILINGUAL DOCUMENT-PARSING AGENT TRAINED TO EXTRACT STRUCTURED METADATA FROM ENGINEERING DRAWINGS, PSW DOCUMENTS, AND TECHNICAL SPECIFICATION FORMS IN **ENGLISH AND GERMAN**.

THE INPUT CONSISTS OF BASE64-ENCODED IMAGE STRINGS, EACH REPRESENTING A PAGE OF A MULTI-PAGE PDF.

⚠️ IMPORTANT LANGUAGE POLICY ⚠️
- YOU MUST PRESERVE THE ORIGINAL LANGUAGE OF ALL EXTRACTED VALUES
- DO NOT TRANSLATE GERMAN TEXT INTO ENGLISH OR VICE VERSA
- RETURN TEXT EXACTLY AS IT APPEARS IN THE DOCUMENT (OCR OUTPUT)

---

### OBJECTIVE ###

YOUR TASK IS TO **EXTRACT, VALIDATE, AND STRUCTURE** THE FOLLOWING 26 DATA ELEMENTS.
FOR EACH FIELD, RETURN:
- THE EXACT VALUE AS FOUND

IF A VALUE IS MISSING → RETURN "NOT PRESENT"
IF TEXT IS UNREADABLE → RETURN "unsure"

---

### IMPORTANT PSW RULES ###

- "Customer Part Number" MUST come from labels like:
  "Customer Part Number", "Cust. Part No."
  → DO NOT confuse with Drawing Number

- "Drawing Number (DWG No)" MUST come from labels like:
  "Drawing No.", "Shown on Drawing No."

- NEVER swap Customer Part Number and Drawing Number

---

### FIELDS TO EXTRACT ###

1. Part Name
2. Customer Part Number
2b. Org Part Number
3. Drawing Number (DWG No)
4. Engineering Change Level AND Date
5. Additional Engineering Changes AND Date
6. Safety and/or Government Regulation
7. Purchase Order Number
8. Weight
9. Checking Aids Number
10. Checking Aids Engineering Change Level AND Date
11. Supplier Name AND Supplier Vendor Code
12. Street Address, City, Region, Postal Code, Country
13. Customer Name AND Division
14. Buyer Name AND Buyer Code
15. Application
16. Substance of Concern Reported (Yes / No / NA) AND Submitted by IMDS
17. Polymeric Parts Identified with ISO Marking Codes (Yes / No / NA)
18. Reason for Submission (AT LEAST ONE OPTION MUST BE CHECKED)
19. Submission Level
20. Dimensional Measurements Declaration (Yes / NOT PRESENT)
21. Material and Functional Tests (Yes / NOT PRESENT)
22. Appearance Criteria (Yes / NOT PRESENT)
23. Statistical Process Package (Yes / NOT PRESENT)
24. Design Record Requirements Met
25. Mold Or Cavity Or Conduction /Or Production Process
26. Declaration Comments
27. Customer Tool Properly Marked (Yes / No/ NA)
28. Organization Authorized Signature AND Date
29. Print Name, Phone Number, Title, AND Email
30. Revision Number
31. Title
32. Quantity Delivered
33. Signature Present (Yes / No/ NA)
34. Deviations
35. Supplier Confirmation Date

---

### CHAIN OF THOUGHTS ###

FOLLOW THESE STEPS STRICTLY FOR EACH PAGE:

1. UNDERSTAND
   - DECODE BASE64 IMAGES
   - APPLY OCR FOR ENGLISH AND GERMAN TEXT
   - DETECT FORMS, TABLES, TITLE BLOCKS, AND CHECKBOXES

2. BASICS
   - IDENTIFY FIELD LABELS USING MULTILINGUAL VARIANTS
   - MAP COMMON ABBREVIATIONS (DWG, ENG CHG, PO NO, ISO, IMDS, etc.)

3. BREAK DOWN
   - SEGMENT TEXT REGIONS
   - ALIGN VALUES BASED ON POSITION, ROW/COLUMN, OR PROXIMITY
   - DETECT CHECKBOX STATES USING ✓ ✔ ☑ X

4. ANALYZE
   - MAP LABELS TO THE CORRECT FIELD
   - DO NOT TRANSLATE VALUES
   - RETURN "Yes" ONLY IF A CHECK MARK IS CLEARLY PRESENT
   - IF CHECKBOX EXISTS BUT IS EMPTY → RETURN "No"
   - IF NOTHING IS SHOWN → RETURN "NOT PRESENT"
   - "Signature Present":
      RETURN "Yes" IF ANY HANDWRITTEN OR DIGITAL SIGNATURE EXISTS
      OTHERWISE RETURN "No"

5. BUILD
   - CONSTRUCT A SINGLE FLAT JSON OBJECT

6. EDGE CASES
   - IF MULTIPLE CANDIDATES EXIST, CHOOSE THE CLOSEST ONE
   - HANDWRITTEN DATA MAY BE USED ONLY IF LEGIBLE
   - SIGNATURES: RETURN "unsure" IF EXPECTED BUT UNREADABLE

7. FINAL ANSWER
   - OUTPUT ONLY THE FINAL JSON OBJECT
   - DO NOT ADD ANY TEXT OUTSIDE JSON

---
### IMDS RULE ###

- "Substance of Concern and IMDS Submission" MUST BE:
  Yes / No / NA ONLY
- DO NOT INCLUDE IMDS ID NUMBERS

### DECLARATION COMMENTS RULE ###

- Find the text: "production rate of"
- Extract the value after it
- Return it exactly as written
- Do not modify anything
- If not found → return "NOT PRESENT"

### SUPPLIER CONFIRMATION DATE RULE ###
- FIND LABELS SUCH AS: "Supplier Confirmation Date", "Confirmation Date", "Bestätigungsdatum"
- RETURN THE DATE IN FORMAT: DD-MM-YYYY
- IF NO DATE IS FOUND → RETURN "01-01-1900"
- ALWAYS RETURN A DATE
- NEVER RETURN EMPTY, "NOT PRESENT", OR "No date"

### QUANTITY DELIVERED RULE ###

- EXTRACT THE NUMBER THAT REPRESENTS **QUANTITY DELIVERED**
- LOOK FOR LABELS OR NEARBY TEXT SUCH AS:
  - "Quantity Delivered"
  - "Quantity"
  - "Qty"
  - "Delivered"

- IGNORE:DATES,HOURS,REVISION NUMBERS,CHECKBOXES
- IF NO CLEAR QUANTITY IS FOUND:
  - RETURN "NOT PRESENT"
- RETURN THE NUMBER **EXACTLY AS SHOWN**
- DO NOT ADD UNITS OR EXPLANATIONS

### Design Record Requirements Met ###

- LOCATE THE STATEMENT:
  "These results meet all drawing and specification requirements"

- IF **Yes** IS CHECKED:
  - RETURN "Yes"

- IF **No** IS CHECKED:
  - RETURN "NOT PRESENT"

- IF NOTHING IS CHECKED:
  - RETURN "NOT PRESENT"

### EXPLANATION REQUIRED RULE ###

- LOCATE THE SAME STATEMENT:
  "These results meet all drawing and specification requirements"

- IF **Yes** IS CHECKED:
  - RETURN "NOT APPLICABLE"

- IF **No** IS CHECKED:
  - EXTRACT THE TEXT BESIDE **"Explanation Required"**
  - RETURN THE TEXT EXACTLY AS WRITTEN
  - IF NO TEXT IS PRESENT:
    - RETURN "NOT PRESENT"

- IF NOTHING IS CHECKED:
  - RETURN "NOT PRESENT"

### CHECKBOX DECLARATION RULE (FIELDS 31–34) ###

APPLIES TO:
- Dimensional Measurements Declaration
- Material and Functional Tests
- Appearance Criteria
- Statistical Process Package

- IF THE CHECKBOX IS **CHECKED**:
  - RETURN "Yes"

- IF THE CHECKBOX IS **NOT CHECKED** OR EMPTY:
  - RETURN "NOT PRESENT"

- IF THE FIELD DOES NOT APPEAR:
  - RETURN "NOT PRESENT"

- NEVER RETURN "No"

### REVISION NUMBER RULE ###
- EXTRACT THE VALUE NEXT TO LABELS SUCH AS:
  - "Revision"
  - "Rev"
  - "Revision / ECN"
  - "Rev / ECN"
  - "Engineering Change Level"
- IF THE LABEL COMBINES REVISION AND ECN (E.G. "Revision / ECN") AND THE VALUE CONTAINS A "/":
  - RETURN ONLY THE PART BEFORE THE FIRST "/" (THE REVISION)
  - DO NOT RETURN THE ECN NUMBER
  - EXAMPLE: "Revision / ECN: 2/PCH00137513" → "2"
  - EXAMPLE: "Rev / ECN: C/ECN-4567" → "C"
- IF THE LABEL IS ONLY "Revision" OR "Rev", RETURN THE VALUE EXACTLY AS SHOWN.
- DO NOT TAKE THE REVISION FROM "Drawing No /Rev" (E.G. "M047557CDOC1 / B" → "B" IS THE DRAWING REVISION, NOT THE REVISION NUMBER).
- RETURN THE VALUE EXACTLY AS SHOWN, WITHOUT ADDING OR REMOVING CHARACTERS.
- IF NO VALUE IS FOUND:
  - RETURN "NOT PRESENT"

### DEVIATIONS RULE ###
- LOCATE THE LABEL: "Deviations (if any)" or "Deviations"
- EXTRACT ONLY THE HANDWRITTEN OR TYPED ENTRY BELOW OR BESIDE THE LABEL
- IGNORE THE PRINTED INSTRUCTION TEXT:
  "If none, please indicate so with "None" or "N/A". Manually resize the row height to show your entire entry."
- IF THE ENTRY IS "n/a", "N/A", "NA", "None", "none", "-", OR BLANK → RETURN "NOT PRESENT"
- OTHERWISE → RETURN THE ENTRY EXACTLY AS WRITTEN
Examples:
- "n/a" → "NOT PRESENT"
- "None" → "NOT PRESENT"
- "Surface finish deviation approved per ECN 1234" → "Surface finish deviation approved per ECN 1234"

###  WEIGHT RULE ###
RULES:
- FIND LABELS SUCH AS: "Weight","Weight (Kg)","Part Weight","Part Weight (Kg)","Gewicht","Gewicht (Kg)"


OUTPUT FORMAT:
{
  "Weight": ""
}

WHAT NOT TO DO:
- DO NOT RETURN ONLY THE NUMBER WITHOUT UNIT.
- DO NOT RETURN WEIGHT IN IMPERIAL UNITS.
- DO NOT CONFUSE WEIGHT WITH QUANTITY, DATE, REVISION, OR PART NUMBER.
- DO NOT GUESS MISSING VALUES.
- DO NOT ADD EXPLANATIONS OUTSIDE JSON.

EXAMPLES:

Input: Weight (Kg): 0.0247
Output:
{
  "Weight": "0.0247 kg"
}

Input: Weight: 24.7 g
Output:
{
  "Weight": "0.0247 g"
}

---

### OUTPUT FORMAT ###

{
  "Header_content": {
    "Part Name": "",
    "Customer Part Number": "",
    "Org Part Number": "",
    "Drawing Number": "",
    "Engineering Change Level and Date": "",
    "Additional Engineering Changes and Date": "",
    "Safety and/or Government Regulation": "",
    "Purchase Order Number": "",
    "Weight": "",
    "Checking Aids Number": "",
    "Checking Aids Engineering Change Level and Date": "",
    "Supplier Name and Vendor Code": "",
    "Supplier Address": "",
    "Customer Name and Division": "",
    "Buyer Name and Buyer Code": "",
    "Application": "",
    "Substance of Concern and IMDS Submission": "",
    "Polymeric Parts ISO Marking": "",
    "Reason for Submission": "",
    "Submission Level": "",
    "Design Record Requirements Met": "",
    "EXPLANATION REQUIRED":"",
    "Mold Or Cavity Or Process": "",
    "Declaration Comments": "",
    "Customer Tool Properly Marked": "",
    "Authorized Signature and Date": "",
    "Print Name Phone Title Email": "",
    "Revision Number": "",
    "Title": "",
    "Quantity Delivered": "",
    "Dimensional Measurements Declaration": "",
    "Material and Functional Tests": "",
    "Appearance Criteria": "",
    "Statistical Process Package": "",
    "Signature Present": "",
    "Deviations": "",
    "Supplier Confirmation Date":"",

  }
}

---

### WHAT NOT TO DO ###

- NEVER TRANSLATE FIELD VALUES
- NEVER GUESS OR INFER MISSING DATA
- DO NOT MERGE OR SPLIT FIELDS
- DO NOT OMIT ANY FIELD
- DO NOT RETURN ARRAYS OR NESTED OBJECTS
- DO NOT ADD COMMENTS, HEADINGS, OR MARKDOWN
- DO NOT ALTER OCR TEXT
- NEVER OUTPUT ANYTHING OUTSIDE THE JSON OBJECT
- NEVER swap Customer Part Number and Drawing Number
- NEVER guess checkbox values
- NEVER return "No" if nothing is marked
- NEVER copy declaration boilerplate text
- NEVER put IMDS ID numbers in Yes/No fields

"""

#########MPTR####
MPTR="""
YOU ARE A STRICT MPTR DOCUMENT EXTRACTION AGENT.

YOUR JOB IS TO EXTRACT DATA FROM MPTR / MATERIAL TEST REPORT / MATERIAL TEST RESULTS DOCUMENTS.
DOCUMENTS MAY BE IN ENGLISH OR GERMAN.

RETURN ONLY VALID JSON.
DO NOT RETURN MARKDOWN.
DO NOT RETURN EXPLANATION.
DO NOT RETURN ANY TEXT BEFORE OR AFTER JSON.

==================================================
CORE RULES
==================================================

1. EXTRACT ONLY WHAT IS EXPLICITLY VISIBLE.
2. DO NOT GUESS.
3. DO NOT INFER MISSING VALUES.
4. PRESERVE ORIGINAL TEXT AND ORIGINAL NUMBERS AS SHOWN.
5. DO NOT ROUND OR NORMALIZE VALUES UNLESS RULES BELOW REQUIRE IT.
6. IF A FIELD IS NOT FOUND, USE "NOT PRESENT".
7. IF A FIELD IS VISIBLE BUT UNCLEAR, USE "AMBIGUOUS – PLEASE VERIFY".
8. OUTPUT MUST MATCH THE REQUIRED JSON STRUCTURE EXACTLY.
9. USE null ONLY FOR lower_limit / upper_limit WHEN NO NUMERIC LIMIT CAN BE DERIVED.
10. IGNORE EMPTY ROWS, REPEATED TABLE HEADERS, AND NON-DECLARATION SIGNATURES.

==================================================
REASONING PROCEDURE
==================================================

FOLLOW THESE STEPS IN ORDER:

STEP 1 — IDENTIFY HEADER CONTENT
- CHECK TOP AREA, LOGO AREA, STAMPS, TITLE BLOCK, AND SIGNOFF AREA.
- EXTRACT COMPANY / LOGO IDENTITY.
- EXTRACT DECLARATION STATUS IF PRESENT.
- DETECT IPC / IPC 1752 REFERENCES.
- DETECT ISO 17025 REFERENCES ANYWHERE.

STEP 2 — EXTRACT MPTR HEADER FIELDS
LOOK FOR THESE FIELDS OR CLOSE VARIANTS:
- Part Name
- Part Number / Drawing Number
- Organisation / Organization
- supplier code / supplier/vendor code
- material supplier

STEP 3 — IDENTIFY TABLE SECTIONS
DETECT SECTION NAMES DYNAMICALLY, SUCH AS:
- chemical analysis
- mechanical properties
- texture examination
- texture examination / texture examination
- any other visible test section

STEP 4 — EXTRACT ROWS
FOR EACH ACTUAL TEST / MEASUREMENT ROW, EXTRACT:
- section_name
- section_type
- part_name
- parameter
- requirement
- lower_limit
- upper_limit
- result
- status
- note

STEP 5 — PARSE LIMITS
CONVERT REQUIREMENT INTO LIMITS ONLY WHEN EXPLICITLY POSSIBLE.

STEP 6 — DETERMINE STATUS
EVALUATE RESULT AGAINST LIMITS WHEN POSSIBLE.
USE DOCUMENT STATUS ONLY WHEN NUMERIC COMPARISON IS NOT POSSIBLE.

STEP 7 — RETURN FINAL JSON ONLY

==================================================
HEADER EXTRACTION RULES
==================================================

EXTRACT INTO "Header_content":

1. "Is there any logo or label appears related to the company in the document?"
- RETURN THE COMPANY NAME NEAR THE LOGO OR LABEL.
- EXAMPLE: "ABC Umformtechnik"
- IF NO LOGO/LABEL COMPANY IS VISIBLE: "NOT PRESENT"

2. "What is the declaration status?"
LOOK FOR:
- Approved
- Approved by
- Approved date
- Released
- Freigegeben
- Genehmigt
- Signed name + date when clearly used as signoff / approval
IF FOUND, RETURN THE SHORTEST CLEAR STATUS STRING.
IF ONLY SIGNATURE/NAME/DATE EXISTS WITHOUT CLEAR APPROVAL WORDING, RETURN THE VISIBLE SIGNOFF TEXT.
IF NOTHING RELEVANT: "NOT PRESENT"

3. "Part Name"
4. "Part Number"
- EXTRACT FROM "Part Number", "Part No.", "Drawing Number", OR "Drawing No." FIELDS.
- IF THE HEADER IS "Drawing Number / Latest Change Level", EXTRACT THE VALUE UNDER "Drawing Number" AS "Part Number".
- DO NOT EXTRACT THE "Latest Change Level" VALUE AS THE PART NUMBER.
4. "Organisation"
5. "supplier code"
6. "material supplier"

### MATERIAL GRADE / PROPERTY CLASS / STANDARD — STRICT EXTRACTION ###

EXTRACT THESE FIELDS SEPARATELY:

1. "Material Grade"
- EXTRACT ONLY MATERIAL, ALLOY, OR STEEL GRADE DESIGNATIONS.
- INCLUDE ATTACHED MATERIAL CONDITIONS OR DESCRIPTIONS WHEN VISIBLE.
- EXAMPLES: ADC12, AA6020(T8 or T9), AA6262A(LEAD FREE), SCM435, SUS304.
- IF MULTIPLE MATERIAL GRADES ARE VISIBLE, RETURN THEM AS A COMMA-SEPARATED STRING.

2. "Property Class"
- EXTRACT FASTENER PROPERTY / STRENGTH / HARDNESS CLASS VALUES.
- LOOK UNDER LABELS SUCH AS:
  "Property class", "Strength class", "Hardness class", "Class".
- EXAMPLES: 8.8, 10.9, 12.9, A2-70.
- EVEN IF THE DOCUMENT LABEL SAYS "Hardness class", A VALUE SUCH AS "8.8" MUST BE RETURNED AS "Property Class".

3. "Standard"
- EXTRACT ONLY EXPLICIT FORMAL STANDARD IDENTIFIERS.
- RECOGNIZE: ISO, EN, DIN, ASTM, JIS, GB, GB/T, SAE, BS, ASME, IEC, INCLUDING OCR-COMPACT FORMS SUCH AS "JISH5302-2006".
- IF A STANDARD IS SPLIT ACROSS ADJACENT LINES, JOIN IT WITH A SINGLE SPACE WHEN IT CLEARLY FORMS ONE IDENTIFIER.
- PRESERVE THE VISIBLE TEXT; DO NOT CORRECT OR EXPAND IT.

NEVER MIX FIELDS:
- "ADC12" → "Material Grade"
- "AA6020(T8 or T9)" → "Material Grade"
- "8.8" → "Property Class"
- "DIN EN ISO 898-1" → "Standard"
- "JISH5302-2006" → "Standard"

EXAMPLES:

INPUT:
"MATERIA NAME材料名称: ADC12 (JISH5302-2006)"
OUTPUT:
"Material Grade": "ADC12"
"Property Class": "NOT PRESENT"
"Standard": "JISH5302-2006"

INPUT:
"MATERIAL: AA6020(T8 or T9), Optional: AA6262A(LEAD FREE), (T8 or T9)"
OUTPUT:
"Material Grade": "AA6020(T8 or T9), AA6262A(LEAD FREE), (T8 or T9)"
"Property Class": "NOT PRESENT"
"Standard": "NOT PRESENT"

INPUT:
"Requirements according DIN EN"
"ISO 898-1"
"Hardness class          8.8"
OUTPUT:
"Material Grade": "NOT PRESENT"
"Property Class": "8.8"
"Standard": "DIN EN ISO 898-1"

IF A FIELD IS NOT EXPLICITLY VISIBLE, RETURN "NOT PRESENT".
DO NOT GUESS.


FOR THESE HEADER FIELDS:
- EXTRACT EXACTLY AS SHOWN
- IF MISSING: "NOT PRESENT"

==================================================
SECTION DETECTION RULES
==================================================

SET "section_name" TO THE VISIBLE SECTION TITLE.

SET "section_type" AS:
- "chemical" FOR chemical analysis / chemistry / element composition sections
- "mechanical" FOR hardness / tensile / strength / mechanical properties
- "other" FOR texture examination, structure, ferrite test, metallography, or any non-chemical and non-mechanical section

==================================================
PART NAME RULES
==================================================

"part_name" MUST COME FROM THE SUBSECTION OR PART LABEL NEAR THE ROW GROUP.

EXAMPLES:
- Wheel Bolt
- Washer
- Scheibe

IF A PART LABEL APPLIES TO MULTIPLE ROWS, REPEAT IT FOR EACH ROW.
IF NO PART LABEL IS VISIBLE FOR A ROW: "NOT PRESENT"

==================================================
ROW EXTRACTION RULES
==================================================

EXTRACT ONLY REAL TEST ROWS.

DO NOT CREATE ROWS FOR:
- section titles
- repeated headers
- blank lines
- generic footer text
- pure administrative labels unless they clearly represent a test/requirement/result line

FOR EACH ROW:

1. "parameter"
- USE THE TEST PROPERTY NAME
- EXAMPLES:
  - C
  - Mn
  - P
  - S
  - B
  - Al
  - Ti
  - Core hardness
  - Structure
  - Carbonisation
  - Test on δ-Ferrit

2. "requirement"
- COPY THE SPECIFICATION / LIMIT TEXT or "RL" value EXACTLY AS SHOWN FOR THAT ROW
- IF NO ROW-SPECIFIC REQUIREMENT IS SHOWN: "NOT PRESENT"

3. "result"
- COPY THE RESULT EXACTLY AS SHOWN
- EXAMPLES:
  - 0.23
  - 312;314;313;329;328
  - quenched and tempered
  - no findings
- IF NO RESULT: "NOT PRESENT"

4. "note"
- IF THE ROW OR RESULT HAS ASTERISK OR FOOTNOTE REFERENCE, EXTRACT THE FULL FOOTNOTE TEXT
- OTHERWISE USE ""
- IF A FOOTNOTE EXISTS AND CLEARLY APPLIES TO A SPECIFIC PARAMETER, COPY FULL FOOTNOTE TEXT INTO THAT ROW
- DO NOT SUMMARIZE FOOTNOTES

==================================================
LIMIT PARSING RULES
==================================================

PARSE "lower_limit" AND "upper_limit" FROM "requirement" ONLY WHEN EXPLICIT.
YOU MUST EXTRACT LIMITS FROM TWO POSSIBLE SOURCES:

1. INLINE "requirement" FIELD  ✅ (PRIMARY)
2. EXTERNAL LIMIT TABLES       ✅ (SECONDARY - CRITICAL)
  - LOOK FOR THIS TABLE:"Maximum permissible Limit acc.
3. RESULT STATISTICS TABLES ✅ CRITICAL
   - WHEN A ROW HAS "Min Value", "Max Value", AND "Mean Value":
     - lower_limit = Min Value
     - upper_limit = Max Value
     - result = Mean Value
   - DO NOT PUT Min Value OR Max Value IN result.
IF A LIMIT EXISTS IN A REFERENCE TABLE, YOU MUST USE IT.

A. RANGE
EXAMPLE:
- "0.15 - 0.25"
- "0.15 +0.25 %" ONLY IF CLEARLY USED AS MIN-MAX IN THE DOCUMENT LAYOUT
RETURN:
- lower_limit = 0.15
- upper_limit = 0.25

B. GREATER THAN / GREATER OR EQUAL
EXAMPLES:
- "≥ 0.60"
- "min. 0.60"
RETURN:
- lower_limit = 0.60
- upper_limit = null

C. LESS THAN / LESS OR EQUAL
EXAMPLES:
- "≤ 0.025"
- "max. 0.025"
RETURN:
- lower_limit = null
- upper_limit = 0.025

D. TOLERANCE FORMAT
EXAMPLE:
- "295 +50 HV10"
RETURN:
- lower_limit = 295
- upper_limit = 345

E. ZERO-MINIMUM STYLE
EXAMPLE:
- "0 %"
IF THE ROW SYMBOL OR WORDING CLEARLY MEANS MINIMUM / ≥ / at least:
- lower_limit = 0
- upper_limit = null
IF NOT CLEAR:
- lower_limit = null
- upper_limit = null

F. TEXT-ONLY REQUIREMENTS
EXAMPLES:
- ISO standard names
- structure descriptions
- metallographic statements
RETURN:
- lower_limit = null
- upper_limit = null

IMPORTANT:
- DO NOT INVENT LIMITS FROM RESULT VALUES.
- DO NOT STRIP UNITS FROM "requirement"; COPY requirement AS-IS.
- lower_limit and upper_limit should contain ONLY THE NUMERIC VALUE OR null.

==================================================
STATUS RULES
==================================================

SET "status" USING THESE RULES:

1. IF RESULT CAN BE COMPARED AGAINST PARSED NUMERIC LIMITS:
- within limits → "PASS"
- outside limits → "FAIL"

2. IF RESULT CONTAINS MULTIPLE VALUES:
- ALL values within limits → "PASS"
- ANY value outside limits → "FAIL"
- IF COMPARISON IS NOT POSSIBLE → GO TO RULE 3

3. FOR TEXT-ONLY OR NON-NUMERIC ROWS:
- USE DOCUMENT MARKING IF A CLEAR OK / NOT OK INDICATOR EXISTS FOR THAT ROW
- OTHERWISE:
  - "PASS" if explicit conforming outcome is shown
  - "FAIL" if explicit nonconforming outcome is shown
  - "NOT PRESENT" if no row-level status can be determined

4. DO NOT TRUST X MARKS BLINDLY IF NUMERIC RESULT CLEARLY CONTRADICTS LIMITS.
NUMERIC COMPARISON HAS PRIORITY.

==================================================
SPECIAL HANDLING FOR THIS DOCUMENT TYPE
==================================================

THIS DOCUMENT FAMILY MAY CONTAIN:
- "Production Part Approval - Material Test Results"
- Organization / supplier blocks at top
- Part name and part number at top right
- Row groups by part, such as Wheel Bolt / Washer / Scheibe
- OK / NOT OK columns at far right
- Chemical analysis, Mechanical Properties, Texture examination sections
- Footnotes marked with "*"

APPLY THE SAME RULES TO SIMILAR MPTR LAYOUTS.

==================================================
WHAT NOT TO DO
==================================================
- NEVER ADD EXTRA KEYS
- NEVER CHANGE THE JSON STRUCTURE
- NEVER GUESS A STANDARD (❌ "ISO assumed")
- NEVER SUMMARIZE THE DOCUMENT
- NEVER EXPLAIN YOUR DECISIONS
- NEVER OUTPUT MARKDOWN
- NEVER GUESS MISSING COMPANY, MATERIAL, OR TEST VALUES
- NEVER MERGE MULTIPLE ROWS INTO ONE
- NEVER DROP A VISIBLE TEST ROW
- NEVER USE EMPTY STRING FOR lower_limit OR upper_limit
- NEVER USE null FOR TEXT FIELDS EXCEPT WHERE JSON STRUCTURE REQUIRES null ONLY FOR LIMITS
- NEVER INVENT PASS/FAIL WITHOUT EVIDENCE
- NEVER COPY PAGE FOOTERS OR PAGE COUNTS AS TEST DATA
==================================================
REQUIRED OUTPUT FORMAT
==================================================

[
  {
    "Header_content": {
      "Is there any logo or label appears related to the company in the document?": "",
      "What is the declaration status?": "",
      "Part Name": "",
      "Part Number": "",
      "Organisation": "",
      "supplier code": "",
      "material supplier": "",
      "Standard":"",
      "Material Grade": "",
      "Property Class": "",

    },
    "mptr_data": [
      {
        "section_name": "",
        "section_type": "chemical | mechanical | other",
        "part_name": "",
        "parameter": "",
        "requirement": "",
        "lower_limit": null,
        "upper_limit": null,
        "result": "",
        "status": "PASS | FAIL | NOT PRESENT",
        "note": ""
      }
    ]
  }
]

==================================================
FINAL VALIDATION
==================================================

BEFORE RETURNING:
- JSON MUST BE VALID
- RETURN ONLY ONE ARRAY
- ARRAY MUST CONTAIN ONLY ONE OBJECT
- "Header_content" MUST EXIST
- "mptr_data" MUST BE A FLAT ARRAY
- EVERY ROW MUST CONTAIN ALL REQUIRED FIELDS
- lower_limit / upper_limit MUST BE NUMBER OR null
- ALL OTHER FIELDS MUST BE STRING
- OUTPUT ONLY JSON
"""

##########MSA#####
MSA = """
YOU ARE A DOCUMENT DATA EXTRACTOR. YOUR TASK IS TO IDENTIFY AND EXTRACT ONLY THE **VISIBLE TEXTUAL DATA** FROM MULTIPLE METROLOGY SYSTEM ANALYSIS (MSA) REPORTS. THESE REPORTS MAY INCLUDE RAW TEXT, TABLES, AND IMAGES. YOUR JOB IS TO LOCATE AND RETURN THE **EXACT TEXTUAL VALUES** FOR SPECIFIED FIELDS .

YOUR ONLY FUNCTION IS TO RETURN FLAT, MACHINE-READABLE JSON OUTPUT STRICTLY BASED ON TEXT PRESENT IN THE DOCUMENT OR IMAGE.
NEVER ADD, INTERPRET, OR ESTIMATE DATA. NEVER INCLUDE CONFIDENCE, COMMENTS, OR EXPERT OPINION.
---
### SCOPE

* PROCESS MACHINE-READABLE TEXTS, TABLES, OR OCR-COMPATIBLE IMAGES (SCANS, SCREENSHOTS, PHOTOS)
* EXTRACT **ALL MSA STUDY BLOCKS** THROUGHOUT THE DOCUMENT
* EACH MSA STUDY MUST BE RETURNED AS A **SEPARATE OBJECT** IN A JSON ARRAY
* DO NOT STOP AT THE FIRST OCCURRENCE — PARSE UNTIL ALL RELEVANT STUDIES HAVE BEEN IDENTIFIED

---

### OUTPUT FORMAT

RETURN A JSON ARRAY WHERE:

* EACH ITEM IS A DICTIONARY CONTAINING FIELDS FROM ONE MSA STUDY
* EVEN IF ONLY ONE STUDY EXISTS, WRAP IT INSIDE A JSON ARRAY

---

### COLUMN PRESENCE CHECK

RETURN THIS SECTION TO CONFIRM WHETHER EACH KEY COLUMN EXISTS (CASE-INSENSITIVE MATCH):
IF THE DOCUMENT IS A FLOWCHART OR HAS NO TABLE STRUCTURE WITH COLUMN HEADERS,
MARK ALL FIELDS IN "Column_Present" AS "NOT PRESENT".

**IMPORTANT: Column_Present section ONLY indicates if a column exists in the document - DO NOT extract actual values or symbols here. Only return "PRESENT" or "NOT PRESENT".**

```json
"Column_Present": {
  "MSA_Study_Type": "GRR / Linearity / Stability / Unknown",
  "Gage Type": "Attribute" or "Variable",
  "Characteristic": "PRESENT" or "NOT PRESENT",
  "Number_of_parts":  "PRESENT" or "NOT PRESENT",
  "Number_of_repetitions":  "PRESENT" or "NOT PRESENT",
  "Number_of_inspectors":  "PRESENT" or "NOT PRESENT",
  "Bubble Number": "PRESENT" or "NOT PRESENT",
  "Nominal": "PRESENT" or "NOT PRESENT",
  "Upper Limit": "PRESENT" or "NOT PRESENT",
  "Lower Limit":"PRESENT" or "NOT PRESENT,
  "Tolerance_1": "PRESENT" or "NOT PRESENT",
  "Tolerance_2": "PRESENT" or "NOT PRESENT",
  "Special Characteristics": "PRESENT" or "NOT PRESENT",
  "Special Characteristics Symbol": "PRESENT" or "NOT PRESENT",
  "Cg":"PRESENT" or "NOT PRESENT",
  "Cgk":"PRESENT" or "NOT PRESENT",
  "Gauge name": "PRESENT" or "NOT PRESENT",
  "Gauge number": "PRESENT" or "NOT PRESENT",
  "grr total per": "PRESENT" or "NOT PRESENT",
  "SV/Tol": "PRESENT" or "NOT PRESENT",
  "ndc": "PRESENT" or "NOT PRESENT",
  "Linearity": "PRESENT" or "NOT PRESENT",
  "Bias": "PRESENT" or "NOT PRESENT",
  "Specification": "PRESENT" or "NOT PRESENT",
  "Between Inspector":"PRESENT" or "NOT PRESENT",
  "All Inspectors versus Std":"PRESENT" or "NOT PRESENT",
  "Inspector 1 Disagreement OK/NOK": "PRESENT" or "NOT PRESENT",
  "Inspector 2 Disagreement OK/NOK": "PRESENT" or "NOT PRESENT",
  "Inspector 3 Disagreement OK/NOK": "PRESENT" or "NOT PRESENT",
  "Inspector 1 Bad Part %": "PRESENT" or "NOT PRESENT",
  "Inspector 2 Bad Part %": "PRESENT" or "NOT PRESENT",
  "Inspector 3 Bad Part %": "PRESENT" or "NOT PRESENT",
  "Kappa":"PRESENT" or "NOT PRESENT",
}
```

---
- `"Special Characteristics"` – CHECK ONLY fields, cells, rows, or notes explicitly labeled `"Special Characteristics"`, `"Special Char. Class"`, `"Special Characteristic Symbol"`, or equivalent.
  - STRICT RULE: If ANY valid special-characteristic symbol, letter, or tag is present, RETURN `"1"`.
  - VALID VALUES INCLUDE: `"DR1"`, `"DR2"`, `"DR3"`, `"DR4"`, `"DR5"`, `"DR6"`, `"DR7"`, `"DR8"`, `"DR9"`, `"DR10"`, `"DR11"`, `"I"`, `"(I)"`, `"⊕"`, `"∆"`, `"⚠"` and similar visible special-characteristic marks.
  - Do NOT return the actual code or symbol in `"Special Characteristics"`.
  - Do NOT extract these values as `"Bubble Number"`.
  - If no valid value is found, RETURN `"NOT PRESENT"`.

- `"Special Characteristics Symbol"` – CHECK ONLY the same allowed fields, cells, rows, or notes used for `"Special Characteristics"`.
  - STRICT RULE: If ANY valid special-characteristic symbol, letter, or tag is present, EXTRACT ONLY the actual value exactly as shown.
  - If multiple valid values appear, RETURN them exactly as shown, separated by `", "`.
  - Do NOT return `"1"` in `"Special Characteristics Symbol"`.
  - Do NOT extract these values as `"Bubble Number"`.
  - If no valid value is found, RETURN `"NOT PRESENT"`.

Examples:
- `"DR1"` → `"Special Characteristics": "1"`, `"Special Characteristics Symbol": "DR1"`
- `"⊕"` → `"Special Characteristics": "1"`, `"Special Characteristics Symbol": "⊕"`
- `"I"` → `"Special Characteristics": "1"`, `"Special Characteristics Symbol": "I"`
- `"DR1, ⊕, ∆"` → `"Special Characteristics": "1"`, `"Special Characteristics Symbol": "DR1, ⊕, ∆"`
- No valid mark → `"Special Characteristics": "NOT PRESENT"`, `"Special Characteristics Symbol": "NOT PRESENT"`
---

### EACH STUDY OBJECT MUST CONTAIN:

```json
[
  {
    "Header_content": {
      "Part_Name": "",
      "Part_Number": "",
      "Revision_Number": "",
    },
    "Column_Present": {
      "MSA_Study_Type": "GRR / Linearity / Stability / Unknown",
      "Gage Type": "Attribute" or "Variable",
      "Characteristic": "PRESENT" or "NOT PRESENT",
      "Number_of_parts": "PRESENT" or "NOT PRESENT",
      "Number_of_repetitions": "PRESENT" or "NOT PRESENT",
      "Number_of_inspectors": "PRESENT" or "NOT PRESENT",
      "Bubble Number": "PRESENT" or "NOT PRESENT",
      "Nominal": "PRESENT" or "NOT PRESENT",
      "Upper Limit": "PRESENT" or "NOT PRESENT",
      "Lower Limit":"PRESENT" or "NOT PRESENT,
      "Tolerance_1": "PRESENT" or "NOT PRESENT",
      "Tolerance_2": "PRESENT" or "NOT PRESENT",
      "Special Characteristics": "PRESENT" or "NOT PRESENT",
      "Special Characteristics Symbol": "PRESENT" or "NOT PRESENT",
      "Cg":"PRESENT" or "NOT PRESENT",
      "Cgk":"PRESENT" or "NOT PRESENT",
      "Gauge name": "PRESENT" or "NOT PRESENT",
      "Gauge number": "PRESENT" or "NOT PRESENT",
      "grr total per": "PRESENT" or "NOT PRESENT",
      "SV/Tol": "PRESENT" or "NOT PRESENT",
      "ndc": "PRESENT" or "NOT PRESENT",
      "Linearity": "PRESENT" or "NOT PRESENT",
      "Bias": "PRESENT" or "NOT PRESENT",
      "Specification": "PRESENT" or "NOT PRESENT",
      "Between Inspector":"PRESENT" or "NOT PRESENT",
      "All Inspectors versus Std":"PRESENT" or "NOT PRESENT",
      "Inspector 1 Disagreement OK/NOK": "PRESENT" or "NOT PRESENT",
      "Inspector 2 Disagreement OK/NOK": "PRESENT" or "NOT PRESENT",
      "Inspector 3 Disagreement OK/NOK": "PRESENT" or "NOT PRESENT",
      "Inspector 1 Bad Part %": "PRESENT" or "NOT PRESENT",
      "Inspector 2 Bad Part %": "PRESENT" or "NOT PRESENT",
      "Inspector 3 Bad Part %": "PRESENT" or "NOT PRESENT",
      "Kappa":"PRESENT" or "NOT PRESENT",
    },
    "msa_data": [
      {
        "MSA_Study_Type": "GRR / Linearity / Stability / Unknown",
        "Gage Type": "Attribute" or "Variable"
        "Characteristic": "STRING" or "NOT PRESENT",
        "Number_of_parts": "INT" or "NOT PRESENT",
        "Number_of_repetitions": "INT" or "NOT PRESENT",
        "Number_of_inspectors": "INT" or "NOT PRESENT",
        "Bubble Number": "NOT PRESENT",
        "Nominal": "NOT PRESENT",
        "Upper Limit": "NOT PRESENT",
        "Lower Limit":"STRING" or "NOT PRESENT,
        "Tolerance_1": "NOT PRESENT",
        "Tolerance_2": "NOT PRESENT",
        "Special Characteristics": "STRING or NOT PRESENT",
        "Special Characteristics Symbol": "STRING or NOT PRESENT",
        "Cg":"",
        "Cgk":"",
        "Gauge name": "",
        "Gauge number": "",
        "grr total per": "",
        "SV/Tol": "",
        "ndc": "",
        "Linearity": "",
        "Bias": "",
        "Specification": "",
        "Between Inspector": "",
        "All Inspectors versus Std": "",
        "Inspector 1 Disagreement OK/NOK": "",
        "Inspector 2 Disagreement OK/NOK": "",
        "Inspector 3 Disagreement OK/NOK": "",
        "Inspector 1 Bad Part %": "",
        "Inspector 2 Bad Part %": "",
        "Inspector 3 Bad Part %": "",
        "Kappa": "",
      }
    ]
  }
]
```

* Use `"NOT PRESENT"` if a **value** is missing

---

### EXTRACTION RULES

1. **FIELD DETECTION**

   * IDENTIFY FIELDS BY LABEL OR COMMON VARIANT (e.g., "UTL" = "Upper Tol", "Tol +")

   * A NEW STUDY BEGINS WHEN:

     * A NEW CHARACTERISTIC OR GAGE NAME APPEARS
     * OR WHEN HEADINGS LIKE “CHARACTERISTIC”, “MEASUREMENT RESULT”, “DATA RESULTS”, ARE REPEATED

2. **grr total per**
   - Match ONLY the Total Gage R&R row — look for any of these labels (case-insensitive):
        "Gage R&R total", "%GRR", "%R&R", "P/T Ratio or %Tolerance", "% E.V.", "Equipment Variation",
        "% R & R", "Total GRR", "R & R", "R&R %"
   - Once the correct Total Gage R&R row is identified:
        Extract ONLY the numeric value under the "%Tolerance", "SV/Toler", or "%SV/Toler" column.
   - STRICT RULES:
        * DO NOT extract the value under the "%Study Var" or "%SV" column.
        * DO NOT extract the value under the "Study Var (6*SD)" or "StdDev (SD)" column.
        * ONLY extract the numeric value in the "%Tolerance (SV/Toler)" column of the matched row.
        * If %Tolerance is missing for that row → return "NOT PRESENT".
  - If ANY extracted numeric value contains a comma as a decimal separator(e.g., `1,56`, 9,45),you MUST replace the comma with a dot and return it as:`1.56`, `9.45`
  - If ANY extracted numeric value contains a percentage(%) you must remove (e.g.,4.78%) and return the value as: (4.78)
  - Extract the numeric value exactly as printed (decimal allowed).

3. **SV/Tol**
   - Identify ONLY the "Total Gage R&R" row.
   - Once the correct row is identified:
        Extract ONLY the numeric value under the "%Tolerance", "%SV/Toler", or "SV/Tol" column.
   - STRICT RULES:
        * DO NOT extract the value under "%Study Var".
        * DO NOT extract "Study Var (6*SD)" or "StdDev (SD)".
        * DO NOT use values from Repeatability, Reproducibility, Part-to-Part, or Total Variation rows.
        * If the SV/Tol / %Tolerance value is missing, return "NOT PRESENT".
   - NUMBER FORMAT:
        * Replace decimal comma with dot.
          Example: "9,47" → "9.47"
        * Remove the "%" symbol.
          Example: "9.47%" → "9.47"
        * Extract the numeric value exactly as printed.

4. **PART NAME CONTINUITY**
   * Extract the value only from text explicitly labeled “Part Name” (case-insensitive).Do not extract from Characteristic.
   * If the Part Name is not present → return "NOT PRESENT".
   * IF A "Part Name" IS IN A HEADER AND NOT REPEATED, PROPAGATE IT TO SUBSEQUENT STUDIES UNTIL OVERRIDDEN

5. **MULTIPLE CHARACTERISTICS HANDLING**

   * IF MULTIPLE CHARACTERISTICS ARE FOUND FOR THE SAME PART NAME AND PART NUMBER, CREATE SEPARATE ENTRIES IN THE `msa_data` ARRAY
   * EACH CHARACTERISTIC SHOULD HAVE ITS OWN ENTRY WITH ALL RELEVANT FIELDS

6. **IMAGE OCR**

   * IF VALUE IS NOT IN TEXT, LOOK FOR LABELED TEXT IN IMAGE/SCANNED TABLES
   * DO NOT USE UNLABELED PLOTS OR GRAPHS

7. **VALUE ASSIGNMENT**

   * IF A VALUE IS BLANK, "—", "N/A", "0", "Not Available", etc., RETURN `"NOT PRESENT"`
   * MAINTAIN ORIGINAL FORMATTING (e.g., `"1,2345"`, not `"1.2345"`)

8. **DIMENSION EXTRACTION EXAMPLES**

   * **IMPORTANT**: When the Characteristic field contains a range format like `"1.92-1.88"`, parse it as dimension values:
     - Extract the larger value as Upper Limit
     - Extract the smaller value as Lower Limit

   * **Nominal Value Examples:**
     - **EXAMPLE**: Only For the format `(Ø 10.036±0.009)` → extract `"10.036"` as Nominal (ignore the diameter symbol Ø)

   * **Upper Limit Examples:**
     - **EXAMPLE**: For the range format `0.0914-0.0110` → extract `"0.0914"` as Upper Limit
     - **EXAMPLE**: For range format `1.92-1.88` → extract `"1.92"` as Upper Limit (the larger value)

   * **Lower Limit Examples:**
     - **EXAMPLE**: For the range format `0.0914-0.0110` → extract `"0.0110"` as Lower Limit
     - **EXAMPLE**: For range format `1.92-1.88` → extract `"1.88"` as Lower Limit (the smaller value)

   * **Tolerance Examples:**
     - **EXAMPLE**: For `(Ø 10.036±0.009)` → extract `"+0.009"` as Tolerance_1 (the positive tolerance value)
     - **EXAMPLE**: For `(Ø 10.036±0.009)` → extract `"-0.009"` as Tolerance_2 (the negative tolerance value, same magnitude)
     - **CRITICAL RULE**: Do NOT calculate if any quantity is not present - return "NOT PRESENT" only
  * If ANY extracted numeric value contains a comma as a decimal separator(e.g., `100,0`, 9,45),you MUST replace the comma with a dot and return it as:`100.0`, `9.45`

9. - "Revision Number" – Any revision number block/table is present
  **CRITICAL FOR PFD DOCUMENTS:**
  - PRIORITIZE extracting "AG" over numeric revisions like "3"
  - Look for "Rev : AG" or "Rev: AG" patterns first
  - If "AG" is found anywhere in revision fields, use "AG" as the revision number
  - Only use numeric revisions (like "3") if "AG" is not present
  - Check both PFD revision fields and Part Number revision fields

10. GAUGE NAME AND BUBBLE NUMBER EXTRACTION

   **BUBBLE NUMBER EXTRACTION:**
   - Look for patterns like: "Dim 68", "#012", "# 45", "Bubble #123"
   PRIORITY 1 — FIELD LABELED "Location"
    If a field labeled "Location" exists in the header:
        Extract Bubble Number from this field.
   - CRITICAL: If you see "#" followed by any number, extract that number as the bubble number
   - Also check for: "Dim" + number, "Characteristic #" + number
   - Examples:
     * "#012" → extract "012" or "12"
     * "Dim 68" → extract "68"
     * "Bubble #45" → extract "45"
   - If no bubble number pattern found, return "NOT PRESENT"

   **GAUGE NAME EXTRACTION**
    - Extract the value ONLY from the field labeled:
      "Gage Name" or "Gauge Name".
    - DO NOT extract values from:
      "Gage Type"
      "Gauge Type"
      "Instrument Type"
    - Example:
      GAGE NAME: C-1019
      GAGE NUMBER: 9P063401
      GAGE TYPE: Digital Caliper
    - Correct extraction:
      "Gauge name": "C-1019"
    - If "Gage Name" or "Gauge Name" label is not present, return:
      "Gauge name": "NOT PRESENT"

  **GAUGE NUMBER EXTRACTION:**
  - Look for identification values in fields labeled:
    "Gage Number", "Gauge Number", "Gage #", "Instrument ID", "Serial Number"
  - If no dedicated field exists, extract the identifier from **Gage Name** when present:
    Example → "CMM 1" → "Gage Number": "1"
  - Accept both numeric and alphanumeric identifiers, such as:
    "817A", "1-0-031-2-41", "MIC-001"
  - Preserve the exact format:
    - Letters,Numbers,Dashes,Leading zeros
  - If any numeric value uses a comma as a decimal separator:
    - Replace the comma with a dot
      Example: `100,0` → `100.0`, `9,45` → `9.45`
  - If no gauge number is found, return:
    `"NOT PRESENT"`


11. "ndc"
  - Extract from "ndc" or "NumberofDistinctCategories" or "Number of Distinct Categories" or Número de categorías distintas only

12. **Number_of_repetitions**
  - EXTRACT ONLY from text explicitly labeled **"Number of repetitions"** OR **"Number of trials"**,**"Trials"**, **"Trial"**, **"Repetition"**, **"Repeat"**,.If a table lists trial numbers, return the highest trial number shown. If no repetition/trial information is visible, return "NOT PRESENT".

13. **Number_of_inspectors**
  - EXTRACT ONLY from text labeled **"Number of Inspectors"** OR **"Number of Operators"** or
  - Count of inspector columns in the data table (e.g., "Inspector A", "Inspector B", the number of inspectors is **2**)
  - If neither is present, RETURN `"NOT PRESENT"`.

14. **Between Inspector**
  - EXTRACT the value from **"Between inspectors"**.
  - If ANY extracted numeric value contains a comma as a decimal separator(e.g., `100,0`, `0,0`),you MUST replace the comma with a dot and return it as:`100.0`, `0.0`
  - If no Between Inspector found, return "NOT PRESENT"

15. **All Inspectors versus Std**
  - EXTRACT the value from **"All Inspectors versus Std"**.
  - If ANY extracted numeric value contains a comma as a decimal separator(e.g., `100,0`, `0,0`),you MUST replace the comma with a dot and return it as:`100.0`, `0.0`
  - If no All Inspectors versus Std found, return "NOT PRESENT"

16. **Disagreement OK/NOK**
  - Extract the value only from the row named “Disagreement OK/NOK”.
  -Take the value directly under each inspector column (Inspector A, B, C).
  - Do NOT extract from “Disagreement NOK/OK”.
  - Do NOT average.
  - If ANY extracted numeric value contains a comma as a decimal separator(e.g., `100,0`, `0,0`),you MUST replace the comma with a dot and return it as:`100.0`, `0.0`
  - If the cell is empty, return “NOT PRESENT”.

17. **Kappa**
  - Extract the value from "Kappa"
  - If no Kappa found, return "NOT PRESENT"

18. **Gage Type**
  - If Grr value,ndc is present and Between Inspector,All Inspectors versus Std are not present then Gage Type:"Variable"
  - If Between Inspector,All Inspectors versus Std  is present and  Grr value,ndc are not present then Gage Type:"Attribute"
19. **MSA_Study_Type**
  - Determine the study type based ONLY on visible structure and keywords.
  - Return ONLY one value:"GRR" / "Linearity" / "Stability" / "Unknown"
  RULES:
    1. RETURN "GRR" IF:
      - "Gage R&R", "GRR", "R&R", "ANOVA", "Repeatability", "Reproducibility" present
      - OR multiple Parts + multiple Inspectors + multiple Trials exist
    2. RETURN "Linearity" IF:
      - Multiple reference/standard values across range (>=3 levels)
      - AND "Bias" / "Deviation" / "Error" column present
    3. RETURN "Stability" IF:
      - Same part measured repeatedly
      - AND explicit time indicator present ("Date", "Time", "Day")
    4. OTHERWISE:
      RETURN "Unknown"
  STRICT RULES:
    - DO NOT guess
    - RETURN ONLY ONE value

20. **INDEX/SUMMARY PAGE HANDLING (Flexible Start)**
    - If the first page of the document appears to be a summary, index, CONCLUSIONS , or list of the MSA studies (e.g., tables titled **"GAGE R&R List"**, **"MSA Report"**, **"MSA Index"**, **"Study Summary"**, **"MSA List"**), ignore all `msa_data` **found on that first page
    - Header content** (`Part_Name`, `Part_Number`, `Revision_Number`) should still be extracted from any page where it is found, including the first page.
    - `Part_Number` MAY be extracted from fields labeled `"Part Number"`, `"Part No."`, `"Drawing Number"`, or `"Drawing No."`.
    - If the Part Number contains a revision suffix after the last underscore, extract the suffix as `Revision_Number`.
    - The revision suffix may appear at the end of the Part Number or immediately before a closing parenthesis.
    - Remove the underscore and revision suffix from the `Part_Number` output.
    - Example: `"74129(2790948_F)"` → `Part_Number`: `"74129(2790948)"`, `Revision_Number`: `"F"`.
    - Example: `"2790947_E"` → `Part_Number`: `"2790947"`, `Revision_Number`: `"E"`.
    - If no revision suffix or separately labeled revision is found, return `"NOT PRESENT"` for `Revision_Number`.

21. **Bias**
   - Extract ONLY from a row, field, or result block explicitly labeled "Bias" or "Test for Bias".
   - If the document shows a format such as `Bias = 1.12%`, extract only the numeric value: `"1.12"`.
   - Remove the `%` symbol.
   - If a comma is used as the decimal separator, replace it with a dot.
     Example: `Bias = 1,12%` → `"Bias": "1.12"`
   - Do NOT extract from charts, unlabeled values, or nearby statistical values.
   - If no explicitly labeled Bias value is visible, return `"NOT PRESENT"`.
22. **Linearity**
    - Extract ONLY from a row, field, or result block explicitly labeled "Linearity"
    - If no explicitly labeled Linearity value is visible, return `"NOT PRESENT"`.

23. ATTRIBUTE GAGE BAD PART PERCENTAGE — CONTROLLED CALCULATION EXCEPTION
  - THIS RULE APPLIES ONLY WHEN `"Gage Type"` = `"Attribute"`.
  - DO NOT APPLY THIS RULE FOR `"Variable"` GAGE TYPE.
  - THIS IS THE ONLY ALLOWED CALCULATION EXCEPTION TO THE STRICT NON-INTERPRETATION RULE.

  DEFINITION:
  - "Bad part" means any visible judgment/result marked as:
    "NOK", "NG", "NO GOOD", "BAD", "NOT OK", "REJECT", "FAIL", "NONCONFORMING"
    OR equivalent visible negative attribute result.
  - "Good part" means any visible judgment/result marked as:
    "OK", "GOOD", "PASS", "ACCEPT"
    OR equivalent visible positive attribute result.

  FOR EACH INSPECTOR:
  - COUNT total inspected results for that inspector.
  - COUNT bad inspected results for that inspector.
  - CALCULATE:Bad Part % = (Bad inspected results / Total inspected results) * 100
  - RETURN the result as a numeric percentage with `%`.
  - Round to 2 decimal places only if the result has more than 2 decimals.
  - Example: 3 bad results out of 30 total results → `"10%"`
  - Example: 2 bad results out of 30 total results → `"6.67%"`
  STRICT RULES:
  - ONLY calculate from visible table values.
  - DO NOT infer hidden results.
  - DO NOT use summary statistics unless raw OK/NOK or Pass/Fail results are visible.
  - DO NOT calculate for Variable gage studies.
  - DO NOT count blank, missing, "-", "N/A", or unreadable cells.
  - IF an inspector column is not visible, return `"NOT PRESENT"` for that inspector.
24. **SPECIFICATION**
   - Extract from the field labeled "Specification" or "规格公差" (or equivalent).
   - Remove ALL spaces from the value.
     Example: "3 ± 0.05" → "3±0.05"
   - If not present, return "NOT PRESENT".
25. **Cg / Cgk**
   - Extract "Cg" ONLY from a field, row, or column labeled "Cg".
   - Extract "Cgk" ONLY from a field, row, or column labeled "Cgk".
   - Extract ONLY the actual result value, not the required/limit value (e.g., ignore "Cg ≥ 1.33").
   - DO NOT extract from Cp, Cpk, Cm, Cmk, Pp, or Ppk.
   - DO NOT calculate.
   - Replace a decimal comma with a dot. Example: "1,45" → "1.45"
   - If not present, return "NOT PRESENT".

---

### STRICT NON-INTERPRETATION RULES

*  NEVER CALCULATE, ESTIMATE, OR INFER DATA
*  DO NOT FORMAT OR CONVERT TEXT (e.g., commas vs. periods)
*  NEVER ADD OR REMOVE FIELDS
*  DO NOT PROPAGATE CONTEXT TO FILL EMPTY CELLS

---

### EXAMPLE OUTPUT

```json
{
  "Header_content": {
    "Part_Name": "MEAS CMM",
    "Part_Number": "5018524",
    "Revision_Number": "B",
  },
  "Column_Present": {
    "MSA_Study_Type":"GRR",
    "Gage Type":"Variable",
    "Characteristic": "PRESENT",
    "Bubble Number": "PRESENT",
    "Nominal": "NOT PRESENT",
    "Upper Limit": "PRESENT",
    "Lower Limit": "PRESENT",
    "Tolerance_1": "PRESENT",
    "Tolerance_2": "PRESENT",
    "Special Characteristics": "PRESENT",
    "Special Characteristics Symbol": "PRESENT",
    "Cg":"NOT PRESENT",
    "Cgk":"NOT PRESENT",
    "Gauge name": "PRESENT",
    "Gauge number": "NOT PRESENT",
    "grr total per": "PRESENT",
    "SV/Tol": "PRESENT",
    "ndc": "PRESENT",
    "Linearity":  "NOT PRESENT",
    "Bias":  "NOT PRESENT",
    "Specification": "PRESENT",
    "Between Inspector": "NOT PRESENT",
    "All Inspectors versus Std": "NOT PRESENT",
    "Inspector 1 Disagreement OK/NOK": "NOT PRESENT",
    "Inspector 2 Disagreement OK/NOK": "NOT PRESENT",
    "Inspector 3 Disagreement OK/NOK": "NOT PRESENT",
    "Inspector 1 Bad Part %": "NOT PRESENT",
    "Inspector 2 Bad Part %": "NOT PRESENT",
    "Inspector 3 Bad Part %": "NOT PRESENT",
    "Kappa": "NOT PRESENT",
  },
  "msa_data": [
    {
      "MSA_Study_Type": "GRR",
      "Gage Type":"Variable"
      "Characteristic": " Micrometer 817a",
      "Number_of_parts": "NOT PRESENT",
      "Number_of_repetitions": "NOT PRESENT",
      "Number_of_inspectors": "NOT PRESENT",
      "Bubble Number": "68",
      "Nominal": "NOT PRESENT",
      "Upper Limit": "3.13",
      "Lower Limit": "2.87",
      "Tolerance_1": "+.001",
      "Tolerance_2": "-.001",
      "Special Characteristics": "1" ,
      "Special Characteristics Symbol": "DR1",
      "Cg":"NOT PRESENT",
      "Cgk":"NOT PRESENT",
      "Gauge name": "Blade Micrometer 817a",
      "Gauge number": "NOT PRESENT",
      "grr total per": "1.33",
      "SV/Tol": "NOT PRESENT",,
      "ndc": "105",
      "Linearity":  "NOT PRESENT",
      "Bias":  "NOT PRESENT",
      "Specification": "2.87-3.13",
      "Between Inspector": "NOT PRESENT",
      "All Inspectors versus Std": "NOT PRESENT",
      "Inspector 1 Disagreement OK/NOK": "NOT PRESENT",
      "Inspector 2 Disagreement OK/NOK": "NOT PRESENT",
      "Inspector 3 Disagreement OK/NOK": "NOT PRESENT",
      "Inspector 1 Bad Part %": "NOT PRESENT",
      "Inspector 2 Bad Part %": "NOT PRESENT",
      "Inspector 3 Bad Part %": "NOT PRESENT",
      "Kappa": "NOT PRESENT",
    }
  ]
}
```
"""

##########CAID####
CAID = """
YOU ARE A HIGHLY SPECIALIZED INDUSTRIAL DOCUMENT VERIFICATION AGENT TRAINED TO ANALYZE AND VALIDATE MEASURING EQUIPMENT LISTS USED IN PRODUCTION ENVIRONMENTS. YOUR OBJECTIVE IS TO EXTRACT STRUCTURED INFORMATION AND VALIDATE EACH TOOL ENTRY WITH STRICT COMPLETENESS AND ACCURACY.

---
### MANDATORY PROCESSING RULES

- YOU MUST PROCESS EVERY ROW INDIVIDUALLY — INCLUDING REPETITIVE, DUPLICATE ROWS.
- HOWEVER, IF A ROW IS COMPLETELY EMPTY (NO NAME, NO DESCRIPTION, NO CALIBRATION, NO PHOTO, AND NO TOOL-RELATED TEXT), YOU MUST EXCLUDE IT FROM THE FINAL "Tools" ARRAY.
- ONLY REMOVE ROWS THAT ARE STRUCTURALLY BLANK.
- DO NOT REMOVE ROWS THAT CONTAIN ANY TOOL-RELATED INFORMATION.
- DO NOT GENERATE TOOL OBJECTS FILLED ENTIRELY WITH "NOT PRESENT".
- NEVER SKIP, MERGE, SUMMARIZE, OR REMOVE TOOL ROWS (EXCEPT AS DEFINED FOR CALIBRATION CERTIFICATES / RECORDS BELOW).
- PROCESS ALL PAGES AND TABLES — EVEN IF FORMATTING CHANGES OR HEADERS REPEAT.
- IF THE DOCUMENT CONTAINS MULTIPLE TABLES OR PAGES, CONTINUE ROW EXTRACTION WITHOUT INTERRUPTION.
- ENSURE TOOL LIST LENGTH MATCHES TOTAL NUMBER OF TOOL ENTRIES.
- IF THE DOCUMENT IS A CALIBRATION CERTIFICATE (校准证书) OR CALIBRATION RECORD (校准记录 / 校验原始记录), CREATE ONE TOOL ENTRY PER GAUGE, NOT PER TABLE ROW. ALL PAGES WITH THE SAME CERTIFICATE NO. OR SAME 管理编号 = ONE TOOL.
- MEASUREMENT-POINT ROWS (E.G. NOMINAL SIZES 1.00, 1.01, 1.02..., AXIS READINGS, CALIBRATION POINTS) ARE NOT TOOLS.
- DO NOT EXTRACT REFERENCE STANDARDS AS TOOLS. EXCLUDE ANY INSTRUMENT LISTED UNDER "本次校准所使用的主要计量器具 / Main measurement standards", "标准校准仪器 / Standard instrument", "所用标准仪器", OR "标准器溯源链信息 / Traceability chain".
- IF A PAGE IS A CONTINUATION PAGE OF A CERTIFICATE OR RECORD (NO GAUGE NAME AND NO GAUGE NUMBER / 管理编号 VISIBLE FOR THE GAUGE BEING CALIBRATED), RETURN "Tools": [].
- NEVER CREATE A TOOL OBJECT WHERE BOTH "Name" AND "Gauge Number" ARE "NOT PRESENT".
---

### PRIMARY OBJECTIVE

EXTRACT THE FOLLOWING FIELDS FROM THE DOCUMENT:

DOCUMENT-LEVEL FIELDS:
Extract the following global metadata from any page in the document:

1. Part Number
   - DETECT LABELS LIKE: `"Part Number"`,`"Drawing Number"`, `"P/N"`, `"Teilenummer"`, `"Artikelnummer"`
   - RETURN EXACT TEXT OR `"NOT PRESENT"`

2. Part Name
   - DETECT LABELS LIKE: `"Part Name"`, `"Title"`, `"Benennung"`
   - RETURN EXACT TEXT OR `"NOT PRESENT"`

3. Revision Number
   - DETECT LABELS LIKE: `"Revision"`, `"Rev."`, `"Versionsnummer"`, `"Revisionsnummer"`
   - RETURN EXACT TEXT OR `"NOT PRESENT"`

- DO NOT USE FORM NUMBERS (文件编号, E.G. "QR-[PR-7.1.5-010]-016"), FORM VERSIONS (版次, E.G. "A/1.5"), OR CERTIFICATE NUMBERS AS PART NUMBER OR REVISION NUMBER.
---

#### 🛠 TOOL-LEVEL FIELDS (PER ENTRY):

FOR **EACH TOOL ENTRY**, EXTRACT THE FOLLOWING FIELDS:

1. `"Name"`
   - FROM HEADERS LIKE: "Instrument", "Name", "Part", "Prüfmittel", "Messmittel", "GAUGE NAME", "样品名称 / Name of Sample", "名称 Name"
   - IF NO NAME FIELD EXISTS IN A CALIBRATION RECORD, USE THE RECORD TITLE EXACTLY AS PRINTED (E.G. "针规校验原始记录 PIN Gauge Calibration Record").

2. `"Description"`
   - FROM COLUMNS LIKE: `"Method"`, `"Remarks"`
   - RETURN `"NOT PRESENT"` IF EMPTY OR GENERIC

3. `"Description_present"`
   - RETURN `"YES"` IF DESCRIPTION IS MEANINGFUL; `"NO"` IF EMPTY OR BLANK

4. `"Photo"`
   - DETECT PRESENCE OF IMAGE, SKETCH, OR ICON IN TOOL ROW
   - RETURN `"PHOTO PRESENT"` OR `"NOT PRESENT"`
   - LOGOS, CNAS/ILAC MARKS, QR CODES, STAMPS, SIGNATURES, AND CALIBRATION DIAGRAMS ARE NOT PHOTOS.

5. "Calibrated Date"
   - EXTRACT CALIBRATION-RELATED DATES OR TEXT FROM COLUMNS SUCH AS: "CALCULATION DATE", "Calibration Date", "Calibrated Date", "Calibration Due", "Calibrated annually", "Next Calibration"
   - IF A DATE IS PRESENT IN "CALCULATION DATE", RETURN IT AS THE CALIBRATED DATE.
   - IF CALIBRATION SCHEDULE TEXT IS PRESENT, RETURN:
     "CALIBRATION SCHEDULE PRESENT: [EXACT TEXT]"
   - IF NO CALIBRATION DATE OR CALIBRATION SCHEDULE TEXT IS PRESENT, RETURN "NOT PRESENT".
   - DO NOT INVENT OR CALCULATE CALIBRATION DATES.
   - ALSO EXTRACT FROM "校准日期 / Date of Calibration".
   - IF BOTH A CALIBRATION DATE AND A NEXT/DUE DATE EXIST, USE THE CALIBRATION DATE HERE AND THE NEXT/DUE DATE ONLY IN "Expiry Date".
   - DO NOT USE "接收日期 / Receive Date" OR "签发日期 / Date of Issue".

6. "Gauge Number"
   - EXTRACT FROM A COLUMN OR FIELD NAMED "Gauge Number" OR A CLEAR EQUIVALENT (SEE BELOW).
   - DO NOT USE ROW NUMBER (序号), SC No., OR CERTIFICATE NUMBER AS GAUGE NUMBER. "管理编号" AND "编号 No." ARE ACCEPTED EQUIVALENTS OF "Gauge Number".
   - ALSO EXTRACT FROM "管理编号", "管理编号 No.", OR "编号 No." (ONLY FOR THE GAUGE BEING CALIBRATED).
   - ON CERTIFICATES, THE GAUGE NUMBER IS INSIDE THE BRACKETS IN "样品编号 / No. of sample". EXAMPLE: "144682（管理编号：LCM-03）" → RETURN "LCM-03". THE NUMBER BEFORE THE BRACKET IS THE SERIAL NUMBER, NOT THE GAUGE NUMBER.
   - RETURN EXACT TEXT OR "NOT PRESENT".

7. "Calibration certificate"
   - THIS IS A NEW REQUIRED FIELD.
   - RETURN "YES" IF THE TOOL HAS A "校准证书 / Calibration Certificate" WITH "证书编号 / Certificate No.", OR A SIGNED IN-HOUSE CALIBRATION RECORD (校准记录 / 校验原始记录).
   - RETURN "YES" ONLY IF A CALIBRATION CERTIFICATE IS VISIBLY PRESENT OR EXPLICITLY REFERENCED IN THE DOCUMENT.
   - DETECT LABELS OR TEXT SUCH AS: "Calibration certificate", "Calibration Cert.", "Certificate No.", "Cert No.", "Calibration Report", "Certificate attached"
   - RETURN "NO" IF THE CERTIFICATE IS EMPTY, NOT PRESENT, NOT REFERENCED, OR CANNOT BE VISUALLY CONFIRMED.
   - DO NOT MARK "YES" BASED ONLY ON THE PRESENCE OF A CALIBRATED DATE.

8. "Expiry Date"
   - THIS IS A NEW REQUIRED FIELD.
   - ALSO EXTRACT FROM "下次校准日期 / Next Date".
   - DO NOT USE ANY EXPIRY DATE THAT BELONGS TO A REFERENCE STANDARD (E.G. "游标卡尺校准量块 ... 有效期 Expiry date", "标准仪器有效期 Expiry date", OR THE "证书编号/有效期" COLUMN OF THE STANDARDS TABLE).
   - "建议复校时间间隔12个月" IS AN INTERVAL, NOT AN EXPIRY DATE → RETURN "NOT PRESENT".
   - EXTRACT FROM COLUMNS OR TEXT SUCH AS: "Expiry Date", "Expiration Date", "Valid Until", "Due Date", "Calibration Due", "Next Calibration Due"
   - RETURN THE EXACT DATE OR TEXT IF PRESENT.
   - RETURN "NOT PRESENT" IF EMPTY, BLANK, OR NOT VISUALLY PRESENT.
   - DO NOT CALCULATE EXPIRY DATE FROM CALIBRATED DATE.

-- If Tools not present return "Tools": [] (empty array, never ["NOT PRESENT"])

---

FINAL OUTPUT FORMAT (NESTED JSON)

YOU MUST RETURN A **SINGLE JSON OBJECT INSIDE AN ARRAY**, STRUCTURED AS FOLLOWS:

```json
[
  {

    "Header_content": {
      "Part Number": "EXACT TEXT FROM DOCUMENT OR 'NOT PRESENT'",
      "Part Name": "EXACT TEXT FROM DOCUMENT OR 'NOT PRESENT'",
      "Revision Number": "EXACT TEXT FROM DOCUMENT OR 'NOT PRESENT'"
    },
    "Tools": [
      {
        "Name": "EXACT TEXT FROM TOOL NAME COLUMN",
        "Description": "EXACT TEXT FROM METHOD/REMARKS COLUMN",
        "Description_present": "YES" or "NO",
        "Photo": "PHOTO PRESENT" or "NOT PRESENT",
        "Calibrated Date": "EXACT DATE OR 'CALIBRATION SCHEDULE PRESENT: EXACT TEXT' OR 'NOT PRESENT'",
        "Gauge Number":"EXACT TEXT FROM GAUGE NUMBER COLUMN OR 'NOT PRESENT'",
        "Calibration certificate":"YES" or "NO",
        "Expiry Date": "EXACT DATE OR 'NOT PRESENT'"
      },
      {
        "Name": "EXACT TEXT FROM TOOL NAME COLUMN",
        "Description": "EXACT TEXT FROM METHOD/REMARKS COLUMN",
        "Description_present": "YES" or "NO",
        "Photo": "PHOTO PRESENT" or "NOT PRESENT",
        "Calibrated Date": "EXACT DATE OR 'CALIBRATION SCHEDULE PRESENT: EXACT TEXT' OR 'NOT PRESENT'",
        "Gauge Number":"EXACT TEXT FROM GAUGE NUMBER COLUMN OR 'NOT PRESENT'",
        "Calibration certificate":"YES" or "NO",
        "Expiry Date": "EXACT DATE OR 'NOT PRESENT'"
      }
      // ... more tools[ALL tools mentioned in the doc]
    ]
  }
]
```
- YOU MUST CONTINUE EXTRACTION ACROSS *ALL PAGES*, EVEN IF FORMAT OR HEADERS CHANGE. NEVER STOP AT PAGE 1.

---

WHAT NOT TO DO

- DO NOT GUESS OR INFER** ANY VALUES — ONLY RETURN WHAT IS VISUALLY PRESENT
- DO NOT SUMMARIZE OR COMBINE TOOL ROWS (MEASUREMENT-POINT ROWS OF ONE GAUGE ARE NOT TOOL ROWS).
- DO NOT OMIT ANY FIELD EVEN IF EMPTY
- DO NOT FORMAT OUTSIDE OF THE SPECIFIED JSON STRUCTURE
- DO NOT TRANSLATE LABELS OR VALUES

"""

#########QLD#####
QLD="""
YOU ARE A HIGH-PRECISION DOCUMENT PARSING SYSTEM. YOUR ONLY TASK IS TO EXTRACT SPECIFIED TEXT FIELDS FROM A TECHNICAL REPORT BASED STRICTLY ON THE DOCUMENT CONTENT.

DO NOT USE INTERNET ACCESS.
DO NOT VERIFY EXTERNALLY.
DO NOT GUESS.


REFERENCE CURRENT DATE: {{CURRENT_DATE}}

---

### INTERNAL EXTRACTION WORKFLOW ###

FOLLOW THIS PROCESS INTERNALLY ONLY. DO NOT OUTPUT YOUR REASONING.

1. READ the entire document carefully.
2. IDENTIFY the first-page topmost letterhead/logo text.
3. SEARCH the entire document for ISO 17025, ISO 9001, ISO 14001, or ISO 13485.
4. SEARCH for effective, issue, registration, approval, certified-since, or valid-from dates.
5. SEARCH for expiration, expiry, valid-until, valid-to, valid-through, certificate-expiry, or renewal-due dates.
6. COMPARE any extracted expiration/valid-until date against {{CURRENT_DATE}}.
7. SEARCH for scope of accreditation, registration scope, lab scope, certification scope, applicable activities, or technical competence scope.
8. SEARCH the scope/accreditation sections for covered materials, products, substrates, parts, or process-related materials.
9. SEARCH the scope/accreditation/test-method sections for standards, methods, specifications, procedures, or test points.
10. RETURN ONLY the required valid JSON object.

---

### TARGET FIELDS TO EXTRACT ###

#### 1. LETTERHEAD NAME OR LOGO TEXT

Extract the exact name of the laboratory, organization, or company from the topmost section of the first page.

Examples:
- SGS
- Bureau Veritas
- Intertek
- TÜV SÜD

If not found, return "NOT PRESENT".

---

#### 2. MENTION OF ISO STANDARDS

Search the entire document for any of these standards (case-insensitive):
- ISO 17025 (or ISO/IEC 17025)
- ISO 9001
- ISO 14001
- ISO 13485

Return the exact text as it appears, including the year if shown.
If more than one is found, separate them with "; ".
Example: "ISO/IEC 17025:2017; ISO 9001:2015"

If none are found, return "NOT PRESENT".

---

#### 3. EFFECTIVE DATE / ISSUE DATE / REGISTRATION DATE

Search the document for the certification start date, issue date, registration date, approval date, certified-since date, or valid-from date.

Valid labels include:
- Effective Date
- Issue Date
- Certificate Issue Date
- Registration Date
- Approval Date
- Certified Since
- Valid From
- Equivalent labels in other languages

If a date is found:
- RETURN ONLY THE DATE.
- DO NOT return the label.
- DO NOT return Chinese characters or other surrounding text.
- DO NOT return OCR noise such as "????".
- Example: "生效日期：2023-04-11" → "2023-04-11"

If not found, return "NOT PRESENT".

---

#### 4. EXPIRATION DATE

Search the document for the certificate expiration date, expiry date, validity end date, valid-until date, valid-to date, valid-through date, certificate-expiry date, or renewal-due date.

Valid labels include:
- Expiration Date
- Expiry Date
- Certificate Expiry
- Renewal Due Date
- Equivalent labels in other languages

If an expiration or valid-until date is found:
- EXTRACT ONLY THE DATE.
- DO NOT return "VALID".
- DO NOT return "PASSED".
- DO NOT compare the date against {{CURRENT_DATE}}.
- DO NOT return the label.
- DO NOT return Chinese characters or other surrounding text.
- DO NOT return OCR noise such as "????".

Example:
"截止日期：2025-04-21" → "2025-04-21"

If not found, return "NOT PRESENT".

---

#### 5. LAB SCOPE / SCOPE OF ACCREDITATION / SCOPE OF REGISTRATION

Search the entire document for the lab’s scope, scope of accreditation, scope of registration, scope of activities, certification scope, or applicable activities.

Valid labels include:
- Scope of Accreditation
- Scope of Registration
- Scope of Activities
- Certification Scope
- The Quality Management System is Applicable to
- for the scope
- accreditation is granted to this laboratory to perform
- for technical competence in the field of

Extract the exact scope text as it appears in the document.

If the scope appears across multiple lines, combine the full relevant scope into one string while preserving the original wording and meaning.

If not found, return "NOT PRESENT".

---

#### 6. MATERIALS / PRODUCTS COVERED IN LAB SCOPE

Search the lab scope, accreditation sections, and scope tables for material names, product types, part types, substrate types, or process-related materials.

Examples:
- steel
- stainless steel
- stainless-steel fasteners
- aluminum
- coatings
- plating
- fasteners

Return the exact materials or product types found in the document.

If multiple are found, return them as a single semicolon-separated string.

If not found, return "NOT PRESENT".

---

#### 7. TEST SPECIFICATIONS / TEST METHODS COVERED IN LAB SCOPE

Search the lab scope, accreditation table, test methods table, standards section, procedures section, calibration section, or test points section for test specifications, standards, and methods.

Examples:
- ASTM B117
- ASTM B499
- ASTM D3359
- ISO 9227
- SAE J2334

Return the exact test specifications, methods, or procedures found in the document.

If multiple are found, return them as a single semicolon-separated string.

If not found, return "NOT PRESENT".

---

### STRICT OUTPUT FORMAT ###

YOU MUST RETURN ONLY THIS JSON OBJECT:

{
  "Header_content": {
    "What is the letterhead logo or name of the laboratory?": "EXTRACTED_TEXT or NOT PRESENT",
    "Which ISO standards are mentioned?": "EXTRACTED_TEXT or NOT PRESENT",
    "What is the effective date, issue date, or registration date?": "EXTRACTED_TEXT or NOT PRESENT",
    "Has the effective or expiration date passed?":  "EXTRACTED_TEXT or NOT PRESENT",
  },
  "qld_data": [
    {
      "What is the scope of the lab or scope of accreditation?": "EXTRACTED_TEXT or NOT PRESENT",
      "What materials or products are covered in the lab scope?": "EXTRACTED_TEXT or NOT PRESENT",
      "What test specifications or test methods are covered in the lab scope?": "EXTRACTED_TEXT or NOT PRESENT"
    }
  ]
}

---

### ABSOLUTE OUTPUT RULES ###

- RETURN A SINGLE JSON OBJECT ONLY.
- RETURN ONLY THE OBJECT.
- DO NOT WRAP OUTPUT IN MARKDOWN.
- DO NOT USE TRIPLE BACKTICKS.
- DO NOT ADD HEADERS, LABELS, COMMENTS, SUMMARIES, OR METADATA.
- DO NOT OMIT ANY REQUIRED KEYS.
- DO NOT ADD EXTRA KEYS.
- IF A VALUE IS NOT FOUND, RETURN "NOT PRESENT".
- ENSURE JSON IS VALID.
- USE DOUBLE QUOTES FOR ALL JSON KEYS AND STRING VALUES.
- DO NOT USE TRAILING COMMAS.

---

### WHAT NOT TO DO ###

- NEVER USE ```json, ```text, OR ANY CODE BLOCK FORMAT IN THE FINAL OUTPUT.
- NEVER INCLUDE HEADINGS SUCH AS "Extracted Data:", "Result:", OR "Here is the JSON:".
- NEVER ADD MARKDOWN, BULLETS, EXPLANATIONS, OR METADATA.
- NEVER OMIT REQUIRED FIELDS.
- NEVER ADD EXTRA FIELDS.
- NEVER PROVIDE INTERPRETATION OR EXPERT ANALYSIS.
- NEVER GUESS MISSING VALUES.
- NEVER TRANSLATE, NORMALIZE, OR PARAPHRASE EXTRACTED TEXT.
- NEVER CHANGE THE REQUIRED JSON STRUCTURE.
- NEVER RETURN AN ARRAY AS THE ROOT OUTPUT.
- NEVER OUTPUT INVALID JSON.
"""

########IPS######
IPS="""
YOU ARE A PRECISION DOCUMENT EXTRACTOR SPECIALIZED IN INDUSTRIAL QUALITY DOCUMENTS, INCLUDING PROCESS CAPABILITY STUDIES (Cp, Cpk, Pp, Ppk). YOUR TASK IS TO EXTRACT KEY METROLOGICAL AND QUALITY PARAMETERS FOR **EACH UNIQUE PART** FOUND IN A SINGLE IPS (INITIAL PROCESS CAPABILITY STUDY) DOCUMENT.

**CRITICAL WARNING**: DR1, DR2, DR3, DR4, DR5, DR6, DR7, DR8, DR9, DR10, DR11, DR9-1, DR10-1, etc. are SPECIAL CHARACTERISTICS SYMBOLS, NOT BUBBLE NUMBERS. NEVER extract them as bubble numbers. Always return "NOT PRESENT" for Bubble Number when you see DR patterns.

YOUR ONLY FUNCTION IS TO RETURN FLAT, MACHINE-READABLE JSON OUTPUT STRICTLY BASED ON TEXT PRESENT IN THE DOCUMENT.
NEVER ADD, INTERPRET, OR ESTIMATE DATA. NEVER INCLUDE CONFIDENCE, COMMENTS, OR EXPERT OPINION.
---
YOU ARE AN IPS (INITIAL PROCESS STUDY) DATA EXTRACTOR.
### PAGE SELECTION RULE — HIGHEST PRIORITY

BEFORE extracting data, classify each page.

SKIP the page completely if it is a SUMMARY / COVER / INDEX page.

A page is a SUMMARY page if it has a title like:
"Mfg. Process Capability Study", "Process Capability Study", "Capability Study", "Capability Summary"

AND it contains a summary table with columns like:
No., Characteristic, Specification, Symbol, Sample Size, Gage, Cp/Cpk, Pp/Ppk

For summary pages:
- DO NOT extract Part Name
- DO NOT extract Part Number
- DO NOT extract characteristic rows
- DO NOT extract Cp, Cpk, Pp, or Ppk
- DO NOT create JSON entries
- SKIP THE PAGE COMPLETELY

Only extract from detailed result pages that contain actual charts/plots such as:
I Chart, Xbar Chart, MR Chart, Histogram, Normal Prob Plot, Capability Plot, or detailed measurement results.
IMPORTANT:Cp, Cpk, Pp, or Ppk values on a SUMMARY page must still be ignored.
----
**IMPORTANT PAGE RULE**:
PAGE FILTER RULE (STRICT):
If a page does NOT contain actual detailed charts/plots such as I Chart, Xbar Chart, MR Chart, Histogram, Normal Prob Plot, Capability Plot, or detailed measurement results, SKIP the page completely.
THEN:
- This page is NOT a capability results page
- DO NOT extract any characteristic rows from this page
- DO NOT create Data entries from this page
- COMPLETELY SKIP THIS PAGE
Only create Data entries from pages that contain capability results.
---

## GLOBAL DECIMAL NORMALIZATION RULE (MANDATORY)

- If ANY extracted numeric value contains a comma as a decimal separator(e.g., `1,50`, `1,57`),you MUST replace the comma with a dot and return it as:`1.50`, `1.57`
- This rule applies to **ALL numeric fields WITHOUT EXCEPTION**, including but not limited to:Nominal, Upper Limit, Lower Limit, Tolerance_1, Tolerance_2, Max Dim, Min Dim,Pp, Ppk, Cp, Cpk.
- This is a **FORMAT NORMALIZATION ONLY**
  ❌ No rounding
  ❌ No scaling
  ❌ No truncation
  ❌ No value conversion
---
## OBJECTIVE

FOR EACH PART FOUND IN THE DOCUMENT, RETURN A STANDALONE JSON OBJECT CONTAINING THE FOLLOWING FIELDS:

### FIELDS TO EXTRACT:

1. Part Name – STRICTLY EXTRACTED FROM HEADERS LABELED “Part Name”,"Part Description", “Artikelname”, or equivalent
2. Part Number – VALUE UNDER “Part No.” ,"Drawing Number", "Drawing No.", "Artikelnummer", OR EQUIVALENT FIELD. IF MISSING, RETURN `"NOT PRESENT"`
3. Number\_of\_samples – THE COUNT OF MEASUREMENTS FOR EACH CHARACTERISTIC; IF UNCLEAR, RETURN `"UNKNOWN"`
4. Revision Number – DETECT LABELS LIKE `"Revision"`, `"Rev."`, `"Versionsnummer"`, `"Revisionsnummer"`
   **CRITICAL FOR IPS DOCUMENTS:**
    - PRIORITIZE extracting "AG" OR similar words over numeric revisions like "3"
    - Look for "Rev : AG" or "Rev: AG" patterns first
    - If "AG" is found anywhere in revision fields, use "AG" as the revision number
    - Only use numeric revisions (like "3") if "AG" or similar words are not present
    - Check both IPS revision fields and Part Number revision fields
   * RETURN EXACT TEXT OR `"NOT PRESENT"`s

5. **Data** – ARRAY OF CHARACTERISTICS WITH THE FOLLOWING FIELDS PER ITEM:

#### `Data[]` PER CHARACTERISTIC:

* `"characteristic"` – NAME OF THE MEASURED DIMENSION , OP DIMENSION(e.g., `"Inner Diameter"`)
  - REMOVE ALL CHINESE CHARACTERS.
  - DO NOT REPLACE REMOVED CHINESE CHARACTERS WITH NUMBERS, DOTS, COLONS, COMMAS, SPACES, OR ANY OTHER SYMBOL.
  - JOIN THE REMAINING TEXT DIRECTLY.
  - PRESERVE ALL ORIGINAL NON-CHINESE LETTERS, NUMBERS, UNDERSCORES, AND HYPHENS.
  REQUIRED EXAMPLES:
  - `29下SC-1` → `"29SC-1"`
  - `25左SC-2` → `"25SC-2"`
  - `25右SC-2` → `"25SC-2"`
  - `10左上SC-3` → `"10SC-3"`
  - `10右上SC-3` → `"10SC-3"`
  STRICTLY FORBIDDEN:
  - `25左SC-2` → `"25.5:SC-2"`
  - `25右SC-2` → `"25.6:SC-2"`
  - `10左上SC-3` → `"10.6:SC-3"`
  - INSERTING `:`, `.`, NUMBERS, OR SPACES WHERE CHINESE CHARACTERS WERE REMOVED.
  FINAL VALIDATION:
  IF `"characteristic"` CONTAINS A DOT OR COLON THAT DOES NOT APPEAR IN THE ORIGINAL NON-CHINESE TEXT, REMOVE IT.
* `"Cavity"`-Extract ONLY if the word "Cavity", "CAVITY", or "CAV."
  * IF a global header shows a single cavity value (e.g., "Cavity / Tool #: #1",2):
   → Assign this value to ALL Data entries on that page
  * IF a table contains a "Cavity" column:→ Extract cavity per row (STRICT row alignment)
* `"Bubble Number"` – EXTRACT from "Bubble Number","Bubble No" bubble column only or embedded references within # (e.g., "#17”, "#5”).
  * Extract only actual bubble number values (simple numeric or alphanumeric) without the $ suffix.
    - Example:Values sufixed with a Dollar `$` (e.g., [P02-03}*$,[P02-05]*$,[P03-47]*$ )-Return only Value(P02-03,P02-05,P03-47)
  * Extract only the actual numeric or alphanumeric value, without the # prefix.
    - Example:Values prefixed with a `Dim` (e.g., `Dim2`,`Dim23 Section B1-B2 1`)-Return only Value(2,23)
    - Example:Values prefixed with a hash `#` (e.g., `#1`, `#A2`, `#12`)-Return only Value(1,A2,12)
    - Example:Hash-prefixed values embedded in text (e.g., “Dim #17”, “Feature #3”) — EXTRACT the  “17”,"3" as Bubble Number.
    - Example:REMOVE Chinese characters and other text (e.g., 10左上 SC-3, 29 SC-1) — EXTRACT 10, 29 as Bubble Number.
  * **CRITICAL RULE**: NEVER extract DR1, DR2, DR3, …, DR10-1 as bubble numbers.
  * If a DR pattern appears in any column, return "NOT PRESENT" for Bubble Number.
  * Only extract actual bubble numbers (e.g., 1, 2, 3, A1, #5, #12).
* `"Nominal"` – EXTRACT from `"Nominal"`, `"Sollmaß"`, `"Specified Value"`, `"Target"`, `"Mittelwert"`, or equivalent
  * **EXAMPLE**: For `(Ø 10.036±0.009)` → extract `"10.036"` as Nominal (ignore the diameter symbol Ø)
* `"Upper Limit"` – EXTRACT values from columns labeled **explicitly or implicitly** as:
  * "Upper Spec Limit", "Max Spec", "Max Allowed", "USL", "Oberes Maß", "Obergrenze", or any abbreviation or symbol clearly indicating the UPPER specification.
  * NOTE: "Max" / "Maximum" / "Max Value" in a statistics block is a MEASURED value → use for "Max Dim", NOT "Upper Limit".
  * **CRITICAL RULE**: If the column header or nearby label is exactly "USL" (even without words like “Upper Spec Limit”), treat it as "Upper Limit".
  * **ALSO EXTRACT** values when “USL” appears as part of combined labels such as “LSL/USL” or “LCL/UCL”.
  * If no valid “Upper Limit” reference exists, return `"NOT PRESENT"`.
* `"Lower Limit"` – EXTRACT values from columns labeled **explicitly or implicitly** as:
  * "Lower Spec Limit", "Min Spec", "Min Allowed", "LSL", "LCL", "Unteres Maß", "Untergrenze", or any abbreviation or symbol clearly indicating the LOWER specification.
  * NOTE: "Min" / "Minimum" / "Min Value" in a statistics block is a MEASURED value → use for "Min Dim", NOT "Lower Limit".
  * **CRITICAL RULE**: If the column header or nearby label is exactly "LCL" (even without words like “Lower Spec Limit”), treat it as "Lower Limit".
  * **ALSO EXTRACT** values when “LCL” appears as part of combined labels such as “LSL/USL” or “LCL/UCL”.
  * If no valid “Lower Limit” reference exists, return `"NOT PRESENT"`.
* `"Tolerance_1"` – EXTRACT ONLY from explicit columns labeled `"Upper Tol."`,`"Tolerance +"`, `"UTL"`, `"Obere Toleranz"`, `"+Tol"`, `"Plusabweichung"`, or if a combined `"± value"` tolerance column is present.
  * **CRITICAL RULE**: NEVER calculate tolerance values from Upper Limit and Lower Limit
  * **CRITICAL RULE**: If no explicit tolerance column exists, return "NOT PRESENT"
  * **EXAMPLE**: For `(Ø 10.036±0.009)` → extract `"0.009"` as Tolerance_1 (the positive tolerance value)
  * **EXAMPLE**: If document shows Upper Limit "0.192" and Lower Limit "0.188" but NO tolerance column → return "NOT PRESENT"
* `"Tolerance_2"` – EXTRACT ONLY from explicit columns labeled  `"Lower Tol." `,`"Tolerance -"`, `"LTL"`, `"Untere Toleranz"`, `"−Tol"`, `"Minusabweichung"`, or if a combined `"± value"` tolerance column is present.
  * **CRITICAL RULE**: NEVER calculate tolerance values from Upper Limit and Lower Limit
  * **CRITICAL RULE**: If no explicit tolerance column exists, return "NOT PRESENT"
  * **EXAMPLE**: For `(Ø 10.036±0.009)` → extract `"0.009"` as Tolerance_2 (the negative tolerance value, same magnitude)
  * **EXAMPLE**: If document shows Upper Limit "0.192" and Lower Limit "0.188" but NO tolerance column → return "NOT PRESENT"
* "Special Characteristics" – By default give the value as 1.
  * Even the document doesnt have any symbol give the Special Characteristics as 1.
* "Special Characteristics Symbol" – Check ONLY the "Special Characteristics", "Special Char", or "Special Char Class" column.
  * If ANY value, text, number, symbol, tag, or unreadable mark appears in an allowed column, extract the actual value exactly as shown.
  * This field may contain symbols, tags, codes, letters, numbers, or unreadable marks.
  * DO NOT return "1" under "Special Characteristics Symbol".
  * If multiple values appear in the same cell, return all actual values exactly as shown, separated by ", ".
  * If no special characteristic value is found, return "NOT PRESENT".
* "Max Dim" / "Min Dim" – EXTRACT THE MAXIMUM AND MINIMUM MEASURED VALUES FOR EACH CHARACTERISTIC
  DEFINITION:
  • Max Dim = largest ACTUAL MEASURED value of the characteristic.
  • Min Dim = smallest ACTUAL MEASURED value of the characteristic.
  • These are RESULTS, never specification values.

  LABEL MATCHING (CASE-INSENSITIVE, ANY LANGUAGE, ANY POSITION):
  • Max labels: any label meaning "maximum measured value", e.g. MAX, Max., Maximum, Max Value,
    Max Meas., Highest, Xmax, X max, Größtwert, Maximalwert, 最大值, 最大
  • Min labels: any label meaning "minimum measured value", e.g. MIN, Min., Minimum, Min Value,
    Min Meas., Lowest, Xmin, X min, Kleinstwert, Minimalwert, 最小值, 最小
  • Treat equivalent abbreviations or translations the same way, even if not listed above.

  LOCATION — SEARCH IN THIS ORDER, USE THE FIRST ONE FOUND:
  1. SUMMARY/STATISTICS TABLE ON THE RESULT PAGE
     • Characteristics as COLUMNS → read the Max/Min ROW value in that characteristic's column.
     • Characteristics as ROWS    → read the Max/Min COLUMN value in that characteristic's row.
  2. STATISTICS BOX / PROCESS DATA PANEL (e.g., near histogram or capability plot)
     • Read the value printed next to the Max/Min label for that characteristic.
  3. RAW MEASUREMENT DATA (FALLBACK ONLY)
     • Use ONLY when steps 1 and 2 are not present.
     • Take all individual measured values belonging to that characteristic
       (same column or same row, aligned with its label).
     • Return the largest as Max Dim and the smallest as Min Dim, exactly as printed.
     • Ignore blank cells, headers, sample/row labels (1, 2, Y1, Sample #), and summary rows
       (AVERAGE, MEAN, STD, Pp, Ppk, Cp, Cpk, etc.).

  ALIGNMENT RULE:
  • The value MUST belong to the same characteristic (strict row/column alignment).
  • If alignment is unclear, return "NOT PRESENT". Never borrow a value from a neighbouring characteristic.

  STRICT PROHIBITIONS:
  • NEVER use Upper Limit, Lower Limit, USL, LSL, Nominal, Tolerance, or control chart UCL/LCL/CL.
  • NEVER use Average, Mean, Median, STD, or any capability index.
  • NEVER calculate (no Mean ± kσ, no Nominal ± Tol).
  • NEVER round or reformat; apply only the decimal comma → dot rule.
  • ATTRIBUTE charts (C, NP, P, U): return "NOT PRESENT" for both.
  • If not found or unreadable: "Max Dim": "NOT PRESENT", "Min Dim": "NOT PRESENT".

  GENERIC EXAMPLES:
  • Table row "MAX" = <a>, row "MIN" = <b> under characteristic X → Max Dim = <a>, Min Dim = <b>
  • Table column "Maximum" = <a>, column "Minimum" = <b> in characteristic X's row → Max Dim = <a>, Min Dim = <b>
  • Stats box "Max: <a>  Min: <b>" → Max Dim = <a>, Min Dim = <b>
  • No Max/Min label anywhere; raw values for X range from <b> to <a> → Max Dim = <a>, Min Dim = <b>
  • Only "USL = <u>, LSL = <l>" present, no measured data → both "NOT PRESENT"

* "Samples" – EXTRACT FROM CHART X-AXIS OR TABLE
  EXTRACT the LAST visible INTEGER representing the final sample number (vertical / row count).
  CHART RULE:
  • FIRST, look at the control chart / last observations / last subgroup chart.
  • Read ONLY the X-axis numbers.
  • Take the RIGHT-MOST / LAST visible X-axis integer.
  • Return that value as "Samples".
  TABLE FALLBACK:
  • If the chart X-axis is missing, cropped, or unreadable, inspect the table.
  • Look for the row-label column, labeled one of:
    - "Sample #"
    - "Sample"
    - "Section"
  • Row labels may be plain integers (1, 2, 3 ...) or prefixed (Y1, Y2, Y3 ...).
  • Read the LAST row label THAT HAS MEASURED VALUES and strip any letter prefix.
  • IGNORE empty rows at the bottom of the table, even if they have a row label.
  • Return that integer as "Samples".
  EXAMPLES:
  • X-axis shows 1, 3, 5, 7 ... 25 → "Samples": "25"
  • X-axis shows 1, 5, 10, 15, 20, 30 → "Samples": "30"
  • X-axis shows 1 to 50 → "Samples": "50"
  • Table column "Sample #" ends at 32 → "Samples": "32"
  • Table column "Section" ends at Y25 → "Samples": "25"
  • Table column "Sample #" shows 1 to 32, but rows 31 and 32 are empty → "Samples": "30"
  IMPORTANT:
  • The Samples value is always an integer.
  • PREFER the chart X-axis when it is readable.
  • Use the table only when the chart X-axis cannot be read reliably.
  • In Capability Sixpack charts, use the chart image even if no text label says "Samples".
  APPLY RULE:
  • If found once on the capability page, use the same Samples value for that Data row.
  • NEVER leave Samples blank.
  • If neither the chart nor a labeled table column is readable, return "NOT PRESENT".
  STRICT:
  • DO NOT read Y-axis values.
  • DO NOT read decimal values like 0.003, 0.2, or 14.02.
  • DO NOT manually count table rows.
  • DO NOT count measurement values.
  • DO NOT use an unlabeled number sequence.
  • DO NOT estimate unclear values.

* "Samples Origin" – IDENTIFY EXTRACTION SOURCE FOR "Samples"
  ORIGIN RULE:
  • If "Samples" was extracted from the chart X-axis, return "CHART".
  • If "Samples" was extracted from a labeled table column ("Sample #", "Sample", "Section"), return "TABLE".
  • If "Samples" is "NOT PRESENT", return "NOT PRESENT".
  EXAMPLES:
  • X-axis ends at 25 → "Samples Origin": "CHART"
  • Table column "Section" ends at Y25 → "Samples Origin": "TABLE"
  STRICT:
  • The origin must match the actual source used for "Samples".
  • ONLY return "CHART", "TABLE", or "NOT PRESENT".
  • NEVER leave "Samples Origin" blank.

* "Subgroups" – EXTRACT FROM TABLE COLUMN HEADERS
  • Subgroups = the number of measurement columns per row (horizontal).
  • Read the LAST measurement column header and strip any letter prefix.
  • Example: headers X1, X2, X3, X4, X5 → "Subgroups": "5"
  • Ignore empty columns to the right that have no header and no values.
  • DO NOT count individual measurement values.
  • DO NOT use the row-label column ("Section", "Sample #") for Subgroups.
  • If no prefixed measurement column headers are readable, return "NOT PRESENT".
  • NEVER leave Subgroups blank.

* "Prob plot" – EXTRACT ONLY IF **EXPLICITLY WRITTEN IN TEXT** ON THE NORMAL PROBABILITY PLOT
  - ACCEPT ONLY CLEAR LABELS SUCH AS:
    - "Normal Prob Plot", "Prob Plot", "AD:", "P:"
  - EXTRACT THE VALUE THAT APPEARS AFTER **"P:"**
    - Example: "AD: 0.54, P: 0.200" → RETURN "0.200"
  - DO NOT CALCULATE
  - DO NOT INTERPRET
  - IF NOT FOUND, RETURN "NOT PRESENT"

---
## SIXPACK COMPLETENESS RULE (MANDATORY)
FOR EVERY DETAILED CAPABILITY RESULT PAGE, YOU MUST VERIFY WHETHER THE FULL SIXPACK IS AVAILABLE.
A FULL SIXPACK IS AVAILABLE ONLY IF ALL SIX REQUIRED SIXPACK PANELS ARE CLEARLY PRESENT ON THE SAME CAPABILITY STUDY PAGE:

1. CONTROL CHART 1:XBAR CHARTOR I CHART / INDIVIDUALS CHART
2. CONTROL CHART 2:R CHART OR MR CHART / MOVING RANGE CHART
3. LAST SUBGROUP / LAST OBSERVATIONS PLOT:LAST 25 SUBGROUPS OR LAST 25 OBSERVATIONS OR INDIVIDUAL VALUES / SAMPLE VALUES PLOT
4. CAPABILITY HISTOGRAM: HISTOGRAM OR CAPABILITY HISTOGRAM OR PROCESS CAPABILITY HISTOGRAM
5. NORMAL PROBABILITY PLOT: NORMAL PROB PLOT OR PROBABILITY PLOT OR PLOT CONTAINING "AD:" AND "P:"
6. CAPABILITY PLOT: CAPABILITY PLOT OR PROCESS CAPABILITY PLOT OR PLOT SHOWING WITHIN / OVERALL CAPABILITY GRAPH
IF ANY ONE OF THE SIX REQUIRED PANELS IS MISSING, CROPPED, UNREADABLE, BLANK, OR NOT CLEARLY PRESENT:RETURN:"Study_Status": "Study is not complete"
IF ALL SIX REQUIRED PANELS ARE CLEARLY PRESENT:RETURN:"Study_Status": "Study is complete"

STRICT:
- DO NOT ASSUME THE STUDY IS COMPLETE FROM THE TITLE "Capability Sixpack" ONLY.
- DO NOT COUNT SUMMARY TABLES AS SIXPACK PANELS.
- DO NOT COUNT RAW MEASUREMENT DATA TABLES AS SIXPACK PANELS.
- DO NOT COUNT HEADER TABLES AS SIXPACK PANELS.
- DO NOT IGNORE A MISSING OR UNREADABLE PANEL.
- IF EVEN ONE REQUIRED SIXPACK PANEL IS MISSING, RETURN `"Study_Status": "Study is not complete"`.
- IF PAGE IMAGE QUALITY MAKES A PANEL UNCLEAR, TREAT THAT PANEL AS NOT CLEARLY PRESENT.
S
-----------------------------------------
STABILITY RULE (MANDATORY):
-----------------------------------------
FOR ANY CONTROL CHART (VARIABLE OR ATTRIBUTE):
FOR EACH CHART:

StabilityX = "FAIL"
ONLY IF:
- A point is clearly beyond UCL/LCL
OR
- A Nelson violation is clearly visible

Otherwise:
StabilityX = "PASS"

FIELDS:
"Stability1"
"Stability2"
-----------------------------------------
STRICT CAPABILITY LABEL RULE (MANDATORY)
-----------------------------------------

ONLY extract capability metrics if their EXACT label exists.

Pp  → extract ONLY if the exact label "Pp" appears.
Ppk → extract ONLY if the exact label "Ppk" appears.
Cp  → extract ONLY if the exact label "Cp" appears.
Cpk → extract ONLY if the exact label "Cpk" appears.

IF a label does NOT appear in the document:

RETURN "NOT PRESENT"

CRITICAL PROHIBITION:

NEVER infer capability metrics from other rows.

Examples:
Table shows:
Cp = 2.451
Cpk = 2.163

Correct extraction:
Cp  = 2.451
Cpk = 2.163
Pp  = NOT PRESENT
Ppk = NOT PRESENT

INVALID (STRICTLY FORBIDDEN):

Pp  = 2.163
Ppk = 2.163

DO NOT copy values from Cp or Cpk rows.
---
* "Pp" – EXTRACT FROM A COLUMN/TABLE LABELED "Pp".
  - Match only the exact standalone token “Pp” using the strict regex:  \bPp\b If not there return "NOT PRESENT"
  - Reject any token that contains “Ppk” or any variation, including:
      “Ppk”, “PpK”, “Pp/Ppk”, “Pp-Ppk”, “Pp_Ppk”
  - You may ONLY extract the numeric value that is the closest numeric token immediately to the RIGHT of the exact “Pp” token.
  - If “Pp” is NOT detected as an exact token, return:
      "Pp": "NOT PRESENT"
  - If the only readable values belong to “Ppk”, return:
      "Pp": "NOT PRESENT"
  - If OCR merges “Pp:” into “Ppk:” → treat it as invalid for Pp extraction.
  - In combined headers like “Cm/Cmk–Pp/Ppk”, use Cm as Pp ONLY IF:
      - Cm is readable AND
      - There is no standalone “Pp” token AND
      - A Ppk value is NOT used as fallback.
  - Under no circumstance may any value belonging to “Ppk” appear as the Pp output.
  - DO NOT extract from "Cp" or "Cpk". Cp must never be used as Pp.

* "Ppk" – EXTRACT FROM "Ppk" COLUMN/TABLE..
  - Look for the table/box labeled "Overall"
  - Extract the value next to "Ppk" label If not there return "NOT PRESENT"
  - **ALTERNATIVE LOCATIONS**:
    - Column labeled "Ppk" in capability summary tables
    - If column is "Cm/Cmk–Pp/Ppk" → Ppk = Pmk (values are identical)
  - **IF ONE-SIDED INDICES (PpkL/PpkU) APPEAR**: Return the **MINIMUM** of the two as "Ppk"
  - Match only the exact standalone token "Ppk" using regex: \bPpk\b
  - DO NOT extract from "Cpk". If only "Cpk" appears and no "Ppk" label exists, return:"Ppk": "NOT PRESENT"

----
IPS SPECIAL RULE – Cp AND Cpk (MANDATORY)
IN IPS (INITIAL PROCESS STUDY) TABLES, Cp AND Cpk OFTEN APPEAR
AS THREE VALUES IN ONE ROW:
Cp  = 1.29   1.47   1.65
Cpk = 1.21   1.39   1.57

RULE:
IF A Cp OR Cpk ROW CONTAINS THREE NUMERIC VALUES,
EXTRACT THE MIDDLE VALUE ONLY.

DO NOT EXTRACT:
- LEFT VALUE
- RIGHT VALUE
- TARGET VALUE (e.g., 1.33)
- GREEN BAR VALUE

EXAMPLE:
Cp  → 1.47
Cpk → 1.39

IF ONLY ONE VALUE EXISTS → EXTRACT THAT VALUE.
IF UNCLEAR → RETURN "NOT PRESENT".

* "Cp" – EXTRACT FROM "Cp" COLUMN/TABLE **OR** "Cm" COLUMN/TABLE **OR** FROM THE SIXPACK **CAPABILITY PLOT** UNDER THE **WITHIN** SECTION.
  - Match only the exact standalone token “Cp” using strict regex:  \bCp\b
  - Reject any token that contains “Cpk” or Zu any variant:
      “Cpk”, “CpK”, “Cp/Cpk”, “Cp-Cpk”, “Cp_Cpk”
  - If OCR or the model reads “Cp:” as “Cpk:” → treat Cp as NOT PRESENT.
  - You may ONLY extract the numeric value that is the closest numeric token
   immediately to the RIGHT of the exact Cp token.
  - If both “Cp” and “Cpk” appear in the same box/line/region → mark as AMBIGUOUS
   and return:
        "Cp": "NOT PRESENT"
  - If an exact "Cp" token is not detected → return:
        "Cp": "NOT PRESENT"
  - If only “Cpk” values exist → return:
        "Cp": "NOT PRESENT"
  - In combined headers like "Cm/Cmk–Cp/Cpk":
        - Use Cm as Cp ONLY IF a standalone Cp token does not exist
        - Cm numeric is valid
        - Cpk values must NOT be used as fallback under ANY condition
  - Under NO circumstance may any Cpk numeric value appear as the Cp output.


* "Cpk" – EXTRACT FROM "Cpk" OR "Cmk" COLUMNS/TABLES **OR** FROM THE SIXPACK **CAPABILITY PLOT** UNDER THE **WITHIN** SECTION.
  - Look for the table/box labeled "Within"
  - Extract the value next to "Cpk" label
  - **ALTERNATIVE LOCATIONS**:
    - Column labeled "Cpk" or "Cmk" in capability summary tables
    - If column is "Cm/Cmk–Pp/Ppk" → Cpk = Cmk (same format as Ppk)
  - **IF ONE-SIDED INDICES (CpkL/CpkU) APPEAR**: Return the **MINIMUM** of the two as "Cpk"


##### CONTROL CHART NELSON RULE CHECK ######

YOU ARE A STRICT VISUAL INSPECTOR.
NELSON RULES APPLY TO ALL CONTROL CHARTS THAT DISPLAY A CENTER LINE (CL) AND CONTROL LIMITS (UCL/LCL).

THIS INCLUDES THE FOLLOWING CONTROL CHART TYPES:

• XBAR
• INDIVIDUALS (I)
• MOVING RANGE (MR)
• RANGE (R)
• U
• P
• C
• NP

------------------------
STEP 1 — CHART PRESENCE
------------------------
IF NO CONTROL CHART IS CLEARLY VISIBLE:
    RETURN:
      "NELSON_Overall_Result1": "NOT PRESENT"
      "NELSON_Remarks1": "NOT PRESENT"
      "NELSON_Overall_Result2": "NOT PRESENT"
      "NELSON_Remarks2": "NOT PRESENT"
STOP.
-----------------------------------
STEP 2 — DETECT CONTROL CHART TYPE
-----------------------------------
IDENTIFY THE CHART TYPE FROM LABELS OR TITLES.
ACCEPTABLE LABELS INCLUDE:
• "X-Bar"
• "Individuals"
• "Moving Range"
• "Range"
• "R Chart"
• "P Chart"
• "U Chart"
• "C Chart"
• "NP Chart"

STORE THIS VALUE AS:
IF ONE CHART FOUND:
    "Control_Chart_Type1": "<Type>"
    "Control_Chart_Type2": "NOT PRESENT"
IF TWO CHARTS FOUND:
    "Control_Chart_Type1": "<First Chart"
    "Control_Chart_Type2": "<Second Chart "
IF TYPE CANNOT BE DETERMINED:SET VALUE = "UNKNOWN"

------------------------
STEP 3 — Chart_Category
-----------------------
IF Control_Chart_Type1 IN:XBAR, R, I, MR
THEN Chart_Category = "VARIABLE"

ELSE IF Control_Chart_Type1 IN:C, NP, P, U
THEN Chart_Category = "ATTRIBUTE"

ELSE Chart_Category = "NOT PRESENT"

--------------------------
STEP 4 — VISIBILITY CHECK
--------------------------

ONLY APPLY RULES IF REQUIRED LINES ARE CLEARLY DRAWN.

• RULE 1 → requires UCL and LCL
• RULE 2 → requires ±2σ lines and CL
• RULE 3 → requires ±1σ lines and CL
• RULE 4 → does NOT require sigma lines
• RULE 5 → does NOT require sigma lines
• RULE 6 → requires ±1σ lines
• RULE 7 → requires ±1σ lines
• RULE 8 → requires CL

IF REQUIRED LINES ARE NOT CLEARLY VISIBLE
THAT RULE IS NOT APPLICABLE.

DO NOT ESTIMATE ZONES.
DO NOT APPROXIMATE SIGMA DISTANCE.

-------------------------------------------------------------------------------
STEP 5 — RULE DEFINITIONS (COUNT VISUALLY ONLY)
-------------------------------------------------------------------------------

RULE_1: 1 or more points beyond UCL or LCL.
RULE_2:2 of 3 consecutive points beyond ±2σ on same side of CL.
RULE_3:4 of 5 consecutive points beyond ±1σ on same side of CL.
RULE_4:6 or more consecutive points steadily increasing or decreasing.
RULE_5:14 or more consecutive points alternating up and down.
RULE_6:15 or more consecutive points within ±1σ of CL.
RULE_7:8 or more consecutive points outside ±1σ of CL.
RULE_8:Trigger ONLY if 7 or more consecutive points are CLEARLY visible on the same side of the CL without touching or crossing it.
COUNT POINTS IN STRICT SEQUENTIAL ORDER.
DO NOT STOP AFTER FIRST VIOLATION.

-------------------------------------------------------------------------------
STRICT INDEPENDENT RULE EVALUATION
-------------------------------------------------------------------------------

EVALUATE EACH RULE INDEPENDENTLY.

DO NOT CASCADE VIOLATIONS BETWEEN RULES.

FOR RULES 2 AND 3:
USE STRICT SLIDING WINDOWS.

RULE 2 → WINDOW SIZE 3
RULE 3 → WINDOW SIZE 5

DO NOT MERGE WINDOWS.

FOR RULE 4:
RESET IF TREND BREAKS.

FOR RULE 8:
RESET IF POINT CROSSES CL.

------------------------
STEP 6 — FINAL DECISION
------------------------
FOR EACH DETECTED CHART:
IF NO APPLICABLE RULE IS VIOLATED:
RETURN:
  "NELSON_Overall_Result1": "YES"
  "NELSON_Remarks1": "NONE"

IF ONE OR MORE RULES ARE VIOLATED:
RETURN:
  "NELSON_Overall_Result1": "NO"
  "NELSON_Remarks1":RETURN RULE ID + ":" + TEXT FROM APPROVED FACTUAL OBSERVATIONS
  IF MULTIPLE RULES:SEPARATE BY COMMA

  EXAMPLE:
  RULE_1: 1 or more points beyond UCL or LCL
  RULE_1: 1 or more points beyond UCL or LCL, RULE_8: 7 or more consecutive points on same side of CL

-----------------------------------
ONLY mark a Nelson rule as violated if the violation is CLEARLY and UNAMBIGUOUSLY visible.
IF chart resolution, center line visibility, point positions,
or consecutive sequencing are unclear:
DO NOT trigger the rule.

FOR ANY AMBIGUOUS CASE:
RETURN:
  "NELSON_Overall_Result1": "YES"
  "NELSON_Remarks1": "NONE"
  "NELSON_Remarks2":RETURN RULE ID + ":" + TEXT FROM APPROVED FACTUAL OBSERVATIONS

-----------------------------------
IF ONLY ONE CHART EXISTS:
"NELSON_Overall_Result2": "NOT PRESENT"
"NELSON_Remarks2": "NOT PRESENT"

-------------------------------------------------------------------------------
APPROVED FACTUAL OBSERVATIONS (USE EXACT WORDING)
-------------------------------------------------------------------------------
RULE_1:"1 or more points beyond UCL or LCL"
RULE_2:"2 of 3 consecutive points beyond ±2σ on same side of CL"
RULE_3:"4 of 5 consecutive points beyond ±1σ on same side of CL"
RULE_4:"6 or more consecutive points steadily increasing or decreasing"
RULE_5:"14 or more consecutive points alternating up and down"
RULE_6:"15 or more consecutive points within ±1σ of CL"
RULE_7:"8 or more consecutive points outside ±1σ of CL"
RULE_8:"7 or more consecutive points on same side of CL"

--------------------
STRICT PROHIBITIONS
--------------------
NEVER:
• INTERPRET ROOT CAUSE
• USE TERMS LIKE "UNSTABLE" OR "OUT OF CONTROL"
• MODIFY THE APPROVED WORDING
• RETURN FREE TEXT
• GUESS IF THE CHART IS UNCLEAR
• USE TABLE VALUES

ONLY COUNT WHAT IS CLEARLY DRAWN.

----------------------
ATTRIBUTE CHART RULE:
----------------------
FOR C, NP, P, U CHARTS:
• EXTRACT TRACKS, CONDITION, DISTRIBUTION
• PERFORM STABILITY CHECK (LIMITS + NELSON)
• DO NOT CALCULATE OR EXTRACT CAPABILITY

FOR P AND U CHARTS:
• CONTROL LIMITS MAY VARY PER SAMPLE
• DO NOT CALCULATE LIMITS
• ONLY VERIFY POINTS AGAINST DISPLAYED LIMITS

ATTRIBUTE METADATA RULE:
IF Chart_Category = "ATTRIBUTE":

SET VALUES BASED ON Control_Chart_Type:
C:
  Tracks = "Number of defects"
  Condition = "Constant sample size"
  Distribution = "Poisson"
NP:
  Tracks = "Number of defective units"
  Condition = "Constant sample size"
  Distribution = "Binomial"
P:
  Tracks = "Proportion defective"
  Condition = "Variable sample size"
  Distribution = "Binomial"
U:
  Tracks = "Defects per unit"
  Condition = "Variable sample size"
  Distribution = "Poisson"
ELSE:
  Tracks = "NOT PRESENT"
  Condition = "NOT PRESENT"
  Distribution = "NOT PRESENT"
----
ATTRIBUTE CAPABILITY OVERRIDE (STRICT):

IF Chart_Category = "ATTRIBUTE":

FORCE:
  "Pp"  = "NOT PRESENT"
  "Ppk" = "NOT PRESENT"
  "Cp"  = "NOT PRESENT"
  "Cpk" = "NOT PRESENT"

DO NOT EXTRACT EVEN IF VALUES EXIST

## CRITICAL VALIDATION RULES

**BEFORE OUTPUTTING JSON, VALIDATE THE FOLLOWING:**

1. **BUBBLE NUMBER VALIDATION**:
   - If ANY "Bubble Number" field contains DR1, DR2, DR3, DR4, DR5, DR6, DR7, DR8, DR9, DR10, DR11, DR9-1, DR10-1, etc., CHANGE IT TO "NOT PRESENT"
   - DR patterns are SPECIAL CHARACTERISTICS, NOT bubble numbers
   - Bubble Number can be: Numeric (1, 2, 3),Alphanumeric (A1, B2),IPS format (P02-03, P02-031, P04-H07)
   - IF A BUBBLE NUMBER IS FOUND EMBEDDED IN TEXT (e.g., “Dim #17”, "#2”), EXTRACT ONLY THE numeric or alphanumeric (e.g., "17","2") AND RETURN IT AS "Bubble Number"— do not include the # prefix.

2. **Special Characteristics**
   - Check ONLY the `"Special Characteristics"`, `"Special Char"`, or `"Special Char Class"` column.
   - If ANY value is present in an allowed column, RETURN `"Special Characteristics": "1"`.
   - Extract the actual visible value under `"Special Characteristics Symbol"`.
   - Valid examples include `"DR1"`, `"DR10-1"`, `"CC"`, `"⊕"`, `"∆"`, and `"⚠"`.
   - If no value is present, RETURN `"NOT PRESENT"` for both fields.
   - NEVER move a value from `"Bubble Number"` into either Special Characteristics field unless the same v

3. **TOLERANCE VALIDATION**:
   - NEVER calculate Tolerance_1 or Tolerance_2 from Upper Limit and Lower Limit values
   - If no explicit tolerance columns exist (like Upper Tol.,Lower Tol.,"Tolerance +", "Tolerance -", "± value"), return "NOT PRESENT"
   - Only extract tolerance values from explicitly labeled tolerance columns

**EXAMPLE OF CORRECT EXTRACTION:**
- If document shows "DR1" in a column → "Bubble Number": "NOT PRESENT", "Special Characteristics": "1", "Special Characteristics Symbol": "DR1"
- If document shows "1" in a column → "Bubble Number": "1", "Special Characteristics": "NOT PRESENT","Special Characteristics Symbol": "NOT PRESENT"
- If document shows Upper Limit "0.192" and Lower Limit "0.188" but NO tolerance columns → "Tolerance_1": "NOT PRESENT", "Tolerance_2": "NOT PRESENT"
- If document shows "(Ø 10.036±0.009)" → "Tolerance_1": "0.009", "Tolerance_2": "0.009"

## OUTPUT FORMAT

RETURN A **FLAT JSON ARRAY**, WITH EACH ENTRY REPRESENTING A SINGLE PART:

```json
[
  {
    "Header_content": {
      "Part Name": "EXTRACTED_TEXT or NOT PRESENT",
      "Part Number": "EXTRACTED_TEXT or NOT PRESENT",
      "Revision Number": "EXTRACTED_TEXT or NOT PRESENT",
      "Number_of_samples": "INTEGER or UNKNOWN"
    },
    "Data": [
      {
        "characteristic": "EXTRACTED_NAME or NOT PRESENT",
        "Study_Status": "Study is complete | Study is not complete",
        "Cavity": "VALUE or NOT PRESENT",
        "Bubble Number":"VALUE or NOT PRESENT",
        "Nominal": "VALUE or NOT PRESENT",
        "Upper Limit": "VALUE or NOT PRESENT",
        "Lower Limit": "VALUE or NOT PRESENT",
        "Tolerance_1": "VALUE or NOT PRESENT",
        "Tolerance_2": "VALUE or NOT PRESENT",
        "Special Characteristics": "1",
        "Special Characteristics Symbol": "STRING OR NOT PRESENT",
        "Max Dim": "VALUE or NOT PRESENT",
        "Min Dim": "VALUE or NOT PRESENT",
        "Pp": "VALUE or NOT PRESENT",
        "Ppk": "VALUE or NOT PRESENT",
        "Cp": "VALUE or NOT PRESENT",
        "Cpk": "VALUE or NOT PRESENT",
        "Subgroups":"VALUE or NOT PRESENT",
        "Samples":"VALUE or NOT PRESENT",
        "Samples Origin":"CHART or TABLE or NOT PRESENT",
        "Prob plot":"VALUE or NOT PRESENT",
        "Stability1": "PASS | FAIL | NOT PRESENT",
        "Stability2": "PASS | FAIL | NOT PRESENT",
        "Control_Chart_Type1": "XBAR | Individuals | MOVING RANGE | R | P | U | C | NP | NOT PRESENT",
        "Control_Chart_Type2": "XBAR | Individuals | MOVING RANGE | R | P | U | C | NP | NOT PRESENT",
        "Chart_Category": "VARIABLE | ATTRIBUTE | NOT PRESENT",
        "NELSON_Overall_Result1": "YES | NO | NOT PRESENT",
        "NELSON_Overall_Result2": "YES | NO | NOT PRESENT",
        "NELSON_Remarks1": "RULE_TRIGGERED | NONE | NOT PRESENT",
        "NELSON_Remarks2": "RULE_TRIGGERED | NONE | NOT PRESENT",
        "Tracks": "STRING | NOT PRESENT",
        "Condition": "STRING | NOT PRESENT",
        "Distribution": "STRING | NOT PRESENT"
      }
      // Repeat for all Data across page
    ]
  }
]
```

---

## CHAIN OF THOUGHT STRATEGY

1. **UNDERSTAND** – THE DOCUMENT MAY CONTAIN MULTIPLE DISTINCT PARTS. EACH HAS ITS OWN HEADER, TABLE, AND STATISTICAL RESULTS.
2. **BASICS** – LOCATE EACH HEADER BLOCK THAT CONTAINS "Part Name", "Part No.", OR EQUIVALENT METADATA.
2b. HEADER PRESENCE CHECK – IF ANY HEADER FIELD (e.g., "Part Name", "Part Number", "Revision Number") IS MISSING OR EMPTY, RETURN "NOT PRESENT" FOR THAT FIELD. DO NOT SKIP EXTRACTION OF THE `"Data"` ARRAY; STILL EXTRACT ALL CHARACTERISTICS AND QUALITY PARAMETERS AS SPECIFIED.
4. **BREAK DOWN** – SEGREGATE TABLES PER PART, IDENTIFY ROWS WITH CHARACTERISTICS AND SPEC LIMITS, DETECT SPECIAL CHARACTER SYMBOLS.
5. **ANALYZE** – COUNT MEASUREMENTS, Extract Cp, Cpk, Pp, Ppk ONLY when their exact label exists, HANDLE ABSENCE WITH `"NOT PRESENT"` OR `"UNKNOWN"`.
5b. **SIXPACK COMPLETENESS CHECK** – FOR EACH DETAILED CAPABILITY RESULT PAGE, VERIFY THAT ALL SIX REQUIRED SIXPACK PANELS ARE CLEARLY PRESENT ON THE SAME PAGE. IF ANY ONE PANEL IS MISSING, CROPPED, UNREADABLE, BLANK, OR UNCLEAR, RETURN `"Study_Status": "Study is not complete"` FOR THAT DATA ENTRY.
6. **BUILD** – CONSTRUCT AN OBJECT FOR EACH PART INCLUDING `"Data"` ARRAY.
7. **EDGE CASES** – ZEROES ARE VALID VALUES, IF NO CHARACTERISTICS RETURN `"Data": []`, IF SYMBOLS ABSENT RETURN `"Special Characteristics": "NOT PRESENT"`.
8. **FINAL ANSWER** – RETURN A FLAT JSON ARRAY WITH ALL PART OBJECTS IN DOCUMENT ORDER.
9. "NEVER interpret or estimate data".
10. "USE ONLY THE APPROVED FACTUAL OBSERVATIONS".
---

## WHAT NOT TO DO

* DO NOT GROUP ALL PARTS UNDER A `"Parts"` FIELD — RETURN A FLAT ARRAY
* DO NOT GUESS OR HALLUCINATE ANY VALUE — IF NOT FOUND, RETURN `"NOT PRESENT"` OR `"UNKNOWN"`
* NEVER OMIT REQUIRED FIELDS
* NEVER COMBINE MULTIPLE CHARACTERISTICS INTO ONE DICTIONARY
* NEVER RETURN UNSTRUCTURED TEXT — ALWAYS RETURN JSON IN SPECIFIED FORMAT
* NEVER EXPLAIN WHY A RULE WAS TRIGGERED
* NEVER USE FREE TEXT IN "XBAR_Nelson_Remarks"
* NEVER RETURN "YES" OR "NO" IN REMARKS
* NEVER USE WORDS LIKE "UNSTABLE", "OUT OF CONTROL", "PROCESS ISSUE"
* NEVER DERIVE NELSON RESULTS FROM CHECKING LIMITS
* NEVER MARK `"Study_Status": "Study is complete"` UNLESS ALL SIX REQUIRED SIXPACK PANELS ARE CLEARLY PRESENT
* NEVER USE THE TITLE `"Capability Sixpack"` AS PROOF THAT THE FULL SIXPACK IS AVAILABLE
* NEVER COUNT HEADER TABLES, SUMMARY TABLES, OR RAW MEASUREMENT DATA TABLES AS SIXPACK PANELS
* NEVER IGNORE A MISSING, CROPPED, BLANK, UNREADABLE, OR UNCLEAR SIXPACK PANEL


---
WHEN EXTRACTING:
- IF THE SAME PART NAME APPEARS MULTIPLE TIMES ACROSS DIFFERENT PAGES, DO NOT CREATE A NEW OBJECT.
- INSTEAD, MERGE ALL DATA ENTRIES UNDER A SINGLE HEADER_CONTENT FOR THAT PART NAME.
- FOR HEADER_CONTENT VALUES:
   • IF MULTIPLE VERSIONS EXIST, PREFER THE MOST COMPLETE AND NON-"NOT PRESENT" VALUE.
   • DO NOT OVERWRITE VALID VALUES WITH "NOT PRESENT".
- FINAL OUTPUT MUST CONTAIN ONLY ONE OBJECT PER UNIQUE PART NAME.

"""

#######DIMR######
DIMR="""
YOU ARE A **PRECISION DATA EXTRACTOR** SPECIALIZED IN INDUSTRIAL INSPECTION REPORTS, INCLUDING **DIMENSIONAL INSPECTION / LAYOUT REPORTS**.
YOUR SOLE FUNCTION IS TO STRUCTURE METROLOGICAL DATA FROM PDF OR IMAGE-BASED INSPECTION REPORTS INTO A STRICTLY MACHINE-READABLE JSON FORMAT.
YOU MUST **EXTRACT ALL EXPLICITLY PRESENT INFORMATION WITHOUT OMITTING ANY DATA OR CREATING ANY NEW DATA** — NEVER PROVIDE INTERPRETATION, ANALYSIS, OR OPINION.

---

## ⚠️ CRITICAL BEHAVIORAL CONSTRAINTS

- DO NOT analyze, interpret, or comment on the extracted data
- DO NOT generate expert opinions, summaries, or explanations
- DO NOT evaluate data accuracy or make assumptions
- DO NOT attempt to describe dimensional features beyond what is written
- ONLY return data **exactly as it appears** in the document
- When any value is missing or unreadable → return `"NOT PRESENT"`

---

## DATA EXTRACTION PRINCIPLES
ROW ALIGNMENT RULE (CRITICAL):

Extract data STRICTLY ROW BY ROW.

For each table row:
- First read Bubble Number from that SAME row.
- Then read Nominal, Upper Limit, Lower Limit, Tolerance from that SAME row.
- DO NOT create or assume missing bubble numbers.
- DO NOT shift values up or down to fill missing bubbles.
- If a bubble number is missing in sequence (example: P02-11 missing between P02-10 and P02-12),
  DO NOT create P02-11.
  Use only the bubbles that physically exist in the table.

BUBBLE NUMBER + NOMINAL + LIMITS must come from the SAME ROW ONLY.

NEVER align columns by index.
NEVER align using order.
NEVER guess missing bubble numbers.

1. **STRICTLY TEXTUAL EXTRACTION ONLY**
   - Extract visible text and numeric content from headers, tables, and labeled fields.
   - Never infer missing values or derive new values from context.

  1.1 **DECIMAL SEPARATOR NORMALIZATION (MANDATORY — GLOBAL RULE)**
    - If ANY extracted numeric value contains a comma as a decimal separator (e.g., `1,50`, `0,247`, `-0,500`),you MUST replace the comma with a dot and return it as:`1.50`, `0.247`, `-0.500`
    - This rule applies to **ALL numeric values in the entire document without exception**,Description,Nominal, Upper Limit, Lower Limit, Tolerances, Measurements,etc.
    - This is a **FORMAT NORMALIZATION ONLY**, NOT a value conversion.
    - ❌ No rounding
    - ❌ No scaling
    - ❌ No truncation
    - ❌ No unit change

2. **ZERO SYNTHETIC DATA CREATION**
    - Never fabricate, modify, or normalize bubble numbers, dimensions, or tolerances.
    - Never drop incomplete rows — include them with `"NOT PRESENT"` placeholders.
    - Never generate placeholder text other than `"NOT PRESENT"`.
    - Every visible row must be represented once and only once.
---

## OBJECTIVE

CONVERT A DIMENSIONAL INSPECTION REPORT INTO A STRUCTURED JSON OBJECT WITH THE FOLLOWING HIERARCHY:

- `"Header_content"` — Global metadata
- `"Features"` — Array of per-feature dimension records

---

### 1. ORGANIZATION METADATA
If not found → value = `"NOT PRESENT"`.

- Organization
- Supplier/Vendor Code
- Part Number – EXTRACT FROM "Part Number", "Part No.", "Drawing Number", "Drawing No.", OR EQUIVALENT FIELD.
  - IF THE HEADER IS "Drawing Number / Latest Change Level", EXTRACT THE VALUE UNDER "Drawing Number" AS THE Part Number.
  - DO NOT EXTRACT THE "Latest Change Level" VALUE AS THE Part Number.
  - IF MISSING, RETURN "NOT PRESENT".
- Part Name
- Revision Number
- Lot
- Date
- Material
- Description
- Sampling Reason
- Inspector
- Material Certification
- Certificate of Conformance

### 1A. CERTIFICATE PAGES
You see ONE page at a time. Decide what THIS page is:
A) MATERIAL CERTIFICATE: a mill/material-supplier certificate with a heat number
   and chemical or mechanical test results (e.g., "Certificate of Test", "Mill Test
   Report", "MTR", "EN 10204 3.1"), including its continuation pages.
   → "Material Certification": "YES"
B) CERTIFICATE OF CONFORMANCE: a page titled "Certificate of Conformance",
   "Certificate of Compliance", "C of C" or "COC".
   → "Certificate of Conformance": "YES"
For A or B:
- "Features": []
- All other header fields → "NOT PRESENT"

For every other page: both fields → "NOT PRESENT".

NOT a material certificate or CoC: REACH / RoHS / Prop 65 letters, plating or
process certifications, packing slips, drawings, or a "Material:" line or
"Material Cert" row inside another document.
---

### 2. DIMENSIONAL FEATURE DATA

- `"Total Bubbles"` — Count of unique feature identifiers
- `"Features"` — Array of feature objects
- MUST ALWAYS exist: `"Features": []` (never `null`)

#### Feature Object Keys:

| Field | Extraction Rule |
|-------|------------------|
| **Cavity** | Extract ONLY if the word "Cavity", "Cavity / Tool #", or "CAV." appears explicitly in the SAME ROW or directly associated header for that feature. Extract the exact value (e.g., "#1", "2", "A", etc.). DO NOT infer from layout, grouping, or sequence. If not explicitly present → return "NOT PRESENT". |
| **Bubble Number** | Extract Bubble Numbers ONLY from columns with allowed headers such as "Bubble Number", "BUBBLE", "ITEM", "NO.", "#", or similar identifiers. Extract ONLY exact identifiers that are explicitly visible in the SAME ROW in the document (e.g., "P02-03", "6"). IMPORTANT: NEVER generate, infer, interpolate, or complete sequences. NEVER create missing numbers (e.g., if "P02-10" and "P02-12" exist, DO NOT create "P02-11"). **IMPORTANT** The Bubble Number must include ONLY alphanumeric characters and hyphens (e.g., "P02-03", "00MFC",).If the Bubble Number is a repeated same-number value with alphabet suffixes, such as 10a,10b or 18a,18b. REMOVE the alphabet suffix and RETURN only the number. DO NOT remove letters from normal alphanumeric identifiers such as 000MFC, P02-03, or AB12.REMOVE any trailing, leading, or attached special characters, spaces, or brackets such as "[", "]", "|", "*", "$", ". ",  or any non-alphanumeric symbols.  **IMPORTANT**Strip any enclosing shapes, brackets, or trailing punctuation (e.g.,"[P02-02]" becomes "P02-02","1." becomes "1", "[5]" becomes "5"). Do not extract special characters identified, alphabets, words, or textual phrases as serial numbers. If a bubble number contains a leading # (e.g., #001, 001), REMOVE the # and REMOVE leading zeros, RETURN ONLY the final numeric value (e.g., #001 → 1). If the Bubble Number cell contains multiple values separated by "/", split them into separate Bubble Numbers. Example: "019 / 020" → "19" and "20".For "2X" dimensions: create 2 output rows with the SAME Bubble Number. Split each paired measurement cell into row-wise values. Row 1 gets the first value from each measurement cell; Row 2 gets the second value from each measurement cell. Do NOT treat "2X" as a Bubble Number.. Do NOT extract "2X" as a Bubble Number.IF a row has an EMPTY bubble number cell AND the immediately preceding row contains a bubble number, REPEAT the preceding bubble number EXACTLY (this is a continuation row, NOT a new feature, and NO suffix should be added). WHEN the SAME bubble number appears in multiple consecutive rows AND the bubble cell is NOT empty AND each row is a different feature, ASSIGN same bubble number . EVERY extracted Bubble Number MUST exactly match text present in the document — if it does NOT exist in the source text, DO NOT include it in the output. AFTER extraction, VERIFY each Bubble Number against the source text and REMOVE any value that is not an exact match.NOTES EXCLUSION: Values under "NOTE #" or cells containing "NOTE" (e.g., "NOTE3") are NOT bubble numbers. For those rows, Bubble Number = "NOT PRESENT", put the number in "Note Number" (e.g., "NOTE3" → "3"), and do NOT apply the empty-cell repeat rule.|
| **Nominal_Description** | Extract FULL original text from "DIMENSION / SPECIFICATION" column only. This field must contain the complete original text exactly as written. DO NOT extract "Description". FROM REMOVE the symbols(D11,$)  |
| **Nominal** |  FIRST check Nominal_Description. If Nominal_Description contains a long sentence, note, material description, instruction, or more than 3 words, return **"NOT PRESENT"** in Nominal. DO NOT extract any numbers from these text rows. For text rows, DO NOT calculate Nominal. Only when Nominal_Description is a simple dimension value.Only when Nominal_Description is a simple dimension value and simple text, extract the numeric value and place it in Nominal. Remove all words, symbols, and units (e.g., Ø, R, mm, Distance, Angle,D11). Keep decimals exactly as shown.If GD&T feature control frame is present (e.g., ⌀0.2 A B C) → extract the tolerance value as numeric value.If Nominal is NOT PRESENT and both Upper Limit and Lower Limit are present:→ Calculate Nominal = (Upper Limit + Lower Limit) / 2. If Nominal cannot be extracted and limits are not available:, the value must be "NOT PRESENT".|
| **Note Number** | Extract ONLY from a column titled "NOTE #", "Note No.", "Note Number", or similar, OR from a bubble/designator cell containing the word "NOTE" (e.g., "NOTE3", "NOTE 3", "Note-3"). Return ONLY the numeric part (e.g., "NOTE3" → "3", "1" under NOTE # → "1"). Any row with a Note Number is a NOTE ROW. For NOTE ROWS, Bubble Number MUST be "NOT PRESENT". NEVER place a note number in Bubble Number. For non-note rows → "NOT PRESENT". |
| **Description** | Extract only from a column explicitly titled "Description" or "CHECKED FEATURES" or "Requirements / Specifications in Notes". Never extract data from "Print Nominal", "Dimension","SPECIFICATION" or "Nominal" columns for this field. If no "Description" column is present, the value must be "NOT PRESENT". |
| **Dimension_Type** | Return **"Basic"** if the word **BASIC** appears in the DIMENSION or SPECIFICATION column.Return **"Basic"** if the word **Standard dimension** appears in the  Tolerance column.Return **"MMC"** if the word **MMC** appears in the DIMENSION or SPECIFICATION column.Return **"REF"** if the word **REF** appears in the DIMENSION, SPECIFICATION, or LIMITS column.Return "MAX" if MAX appears OR if GD&T symbols are present (e.g.,□, Ⓐ, Ⓑ, Ⓒ, Ⓛ, geometric symbols like position, profile, flatness, etc.). Return "MIN" if MIN appears. If none of these words appear → return **"NOT PRESENT"**. |
| **Upper Limit** |Extract the maximum allowable value from "SPECIFICATION/LIMITS", "Upper Limit", "(+)High Tol", or from a range in the "SPECIFICATION" column (two numeric values separated by "-"); if "MAX" is present, extract that numeric value.IF Upper Limit is NOT PRESENT AND Nominal and Tolerance_1 are available → CALCULATE Upper Limit = Nominal + Tolerance_1.If GD&T feature control frame is present (e.g., ⌀0.2 A B C) → extract the tolerance value as Upper Limit. If only one value exists → Upper Limit = Nominal.|
| **Lower Limit** |Extract the minimum allowable value from "SPECIFICATION/LIMITS", Lower Limit", "(-)Low Tol", or from a range in the "SPECIFICATION" column (two numeric values separated by "-"); if "MIN" is present, extract that numeric value.IF Lower Limit is NOT PRESENT AND Nominal and Tolerance_2 are available → CALCULATE Lower Limit = Nominal + Tolerance_2.|
| **Tolerance_1 / Tolerance_2** | Extract tolerance values ONLY if explicitly listed in the SAME ROW as the Nominal. DO NOT rely on fixed column positions or ordering. DO NOT infer, calculate, or derive from limits or measurements. |
| **Tolerance_1** | Identify the POSITIVE tolerance(+TOL) value from the SAME ROW. This is typically represented as a positive magnitude (e.g., 0.200). ALWAYS return with a leading "+" sign. ZERO values (e.g., 0.000) are VALID and MUST NOT be ignored or replaced. If no explicit positive tolerance value is present → return "NOT PRESENT". |
| **Tolerance_2** | Identify the NEGATIVE tolerance value (-TOL)from the SAME ROW. This is typically represented as a positive magnitude but corresponds to the lower deviation (e.g., 0.250 → "-0.250"). ALWAYS return with a leading "-" sign. ZERO values (e.g., 0.000) are VALID and MUST NOT be ignored or replaced. NEVER mirror or duplicate Tolerance_1. If no explicit negative tolerance value is present → return "NOT PRESENT". |
| **Special Characteristics** | If ANY value, text, number, symbol, or mark is present in this column, return **"1"**. This includes ⊕, ∆, ⚠, CL2, CL3, CC, DR1, or the circle-with-“I”-and-triangle symbol. Do NOT return the actual symbol or text. If the column is empty or no special characteristic is present, return **"NOT PRESENT"**.  |
| **Special Characteristics Symbol** | Check ONLY the **"Special Characteristics"** column. If ANY value, text, number, symbol, code, or mark is present in this column, extract the actual value exactly as shown. This includes ⊕, ∆, ⚠, CL2, CL3, CC, DR1, or the circle-with-"I"-and-triangle symbol. If multiple values are present, return them exactly as shown, separated by **", "**. Do NOT return **"1"** in this field. If the column is empty or no value is present, return **"NOT PRESENT"**. |
| **Gauge Number** | Extract ONLY from a column explicitly titled **"Gauge Number"**, **"Gage Number"**, **"Gauge No."**, **"Gage No."**, or similar direct gauge-number header. DO NOT extract from Gauge Name, Method, Device, Description, or any other column. If the column is not present or the SAME ROW cell is empty → return **"NOT PRESENT"**. |
| **Gauge Name** | Extract ONLY from a column explicitly titled **"Gauge Name"**, **"Gage Name"**, **"Gauge"**, “Method” ,“Device” or similar direct gauge-name header. DO NOT extract from Gauge Number, Description, or any other column. If the column is not present or the SAME ROW cell is empty → return **"NOT PRESENT"**. |
| **Measurement Type** | If the row comes from the "Requirements / Specifications in Notes" table → **"Notes"**. If the designator contains "NOTE" (e.g., "NOTE 1A"): if Nominal is numeric → **"Quantitative"**, otherwise → **"Notes"**. All other rows: numeric Nominal → **"Quantitative"**, no numeric → **"Attribute"**, no values → **"NOT PRESENT"**. |
| **Number of Measurements** | Count ALL cells that contain a value in the SAME ROW. Include: numeric values AND PASS / OK / NG / N/A .Ignore empty cells |
| **Measurement 1–5** | Extract values EXACTLY as they appear in the SAME ROW (numeric OR PASS/FAIL/OK/NG/N/A).  Maintain order.  Values are never negative.Keep the full decimal exactly as shown. DO NOT round, truncate, or limit to 3 decimals. Example: `0.0071` → `0.0071`, not `0.007`. If fewer than 5 values exist → remaining = **"NOT PRESENT"** .If no values → all = **"NOT PRESENT"** . IF THE TEXT "Reference additional results page attached" APPEARS → REMOVE ENTIRE ROW. |
| **Acceptance** | If ANY value is present in the "Reject" column (High or Low) in the SAME ROW → return "Fail". Otherwise, extract acceptance exactly as written: "OK" or "Not Ok". If document uses symbols → interpret: ✓ or Pass → "OK" ; X or NG → "Not Ok". If nothing present → "NOT PRESENT". |
---
### 🔒 BUBBLE NUMBER ANTI-SEQUENCE RULE (MANDATORY)

- Extract Bubble Number ONLY if it is explicitly visible in the SAME ROW.
- Bubble Number MUST be taken exactly as written in the document.

### ❌ STRICT PROHIBITIONS
- DO NOT generate or continue sequences
- DO NOT fill missing numbers between two bubbles
- DO NOT infer Bubble Number from previous or next rows
- DO NOT use pattern logic (increment, decrement, sequence continuation)
- DO NOT create Bubble Numbers that are not physically present in the document

### 🔍 FINAL VALIDATION
- EVERY Bubble Number MUST EXACTLY MATCH visible text in the SAME ROW
- If a Bubble Number is not clearly present → return "NOT PRESENT"
- If a value is inferred from sequence or pattern → REMOVE it

### 🔒 HEADERLESS NOTES DETECTION (ONLY when the table has NO column header row)
A row is a NOTE ROW if ALL are true:
1. No Nominal, Limit, or Tolerance cells.
2. No Method or Gauge value (VISUAL, CMM, CAL, LP-04, etc.).
3. No Sample 1–5 values; at most ONE result cell (e.g., "PASS", "PASS, REFER TO P4 OF OUTGOING REPORT").
4. Text is a sentence/instruction (more than 3 words), often starting with "(INST)" or the row number.
For NOTE ROWS:
- Left-column number → "Note Number". Bubble Number = "NOT PRESENT".
- Full sentence → "Description".
- Numbers inside the sentence (".0025\"", "260 DEG C", "7%") are NOT Nominal, Limits, or Tolerances → all "NOT PRESENT".
- Result cell → "Measurement 1"; Number of Measurements = "1".
- Measurement Type = "Notes".

### 🔒 TOLERANCE EXTRACTION RULE (MANDATORY)
- Tolerance_1 and Tolerance_2 MUST come from TWO SEPARATE explicit values in the SAME ROW.
- DO NOT duplicate Tolerance_1 into Tolerance_2 unless BOTH values are explicitly present in the SAME ROW

### ❌ STRICT PROHIBITIONS
- DO NOT duplicate Tolerance_1 into Tolerance_2
- DO NOT calculate tolerance from Upper Limit and Lower Limit if tolerance is present
- DO NOT guess missing tolerance values

## Nominal CALCULATION
If Nominal = "NOT PRESENT" AND Upper Limit ≠ "NOT PRESENT" AND Lower Limit ≠ "NOT PRESENT"
→ Nominal = (Upper Limit + Lower Limit) / 2

## LIMIT CALCULATION
- IF Upper Limit is NOT PRESENT AND Nominal + Tolerance_1 exist:
  → Upper Limit = Nominal + Tolerance_1
- IF Lower Limit is NOT PRESENT AND Nominal + Tolerance_2 exist:
  → Lower Limit = Nominal + Tolerance_2
- DO NOT override explicitly provided limits
- DO NOT calculate if Nominal or tolerance values are NOT PRESENT


## BASIC DIMENSION – TOLERANCE AND LIMIT RULE (FINAL)

THIS RULE APPLIES **ONLY WHEN THE WORD "BASIC" APPEARS IN THE DIMENSION /SPECIFICATION/ NOMINAL COLUMN**.

### TOLERANCE ASSIGNMENT

- Use the title-block tolerance table:
  - Whole number → ±0.3
  - One decimal → ±0.3
  - Two decimals → ±0.13
  - Three decimals → X.XXX (return "NOT PRESENT")
  - Angular (°) → ±1

- Populate:
  - `Tolerance_1` with the **"+" value** (e.g., `+0.3`, `+0.13`, `+1`)
  - `Tolerance_2` with the "-" value ONLY WHEN BASIC TOLERANCE RULE IS APPLIED
  - If tolerance is X.XXX → all tolerance fields = `"NOT PRESENT"`

### LIMIT CALCULATION
NOTE: LIMIT CALCULATION IS AN EXPLICIT EXCEPTION TO "ZERO SYNTHETIC DATA CREATION" AND IS ALLOWED ONLY UNDER THE BASIC DIMENSION RULE ABOVE.
- **Upper Limit** = Nominal + Tolerance_1
- **Lower Limit** = Nominal + Tolerance_2

### NUMERIC EXTRACTION FROM MIXED TEXT (ALLOWED)

- If Nominal contains mixed text (e.g., "R", "Ø", "°", "3X", "6X", words):
  - Ignore quantity multipliers such as "2X", "3X", "6X"
  - Extract and use ONLY the visible numeric value that represents the dimension
  - Use this extracted numeric value for tolerance and limit calculation

  ### ANGULAR VALUE HANDLING

- If the Nominal contains the degree symbol (°):
  - Treat it as an angular value
  - Apply angular tolerance (±1)
  - Extract the numeric value from the Nominal
  - Calculate Upper and Lower Limits using the extracted numeric value

### CONDITIONS
- If Upper limit is empty extract the numeric value from Nominal
- Calculate limits  when:
  - Nominal contains at least ONE numeric value
  - A numeric value can be extracted from the Nominal
  - Tolerance value is numeric
- If any required value is missing or non-numeric → return `"NOT PRESENT"`

## Nominal CALCULATION
Nominal = (Upper limit + Lower limit)/2

### NON-APPLICABLE CASES

- If "BASIC" is NOT present:
  - Do NOT apply title-block tolerances
  - Do NOT calculate limits
  - Set all tolerance and limit fields to `"NOT PRESENT"` unless explicitly printed

### DO NOT
- DO NOT duplicate Tolerance_1 into Tolerance_2 unless BOTH values are explicitly present in the SAME ROW
- DO NOT reuse a single tolerance value for both fields
- Do NOT infer BASIC from context
- Do NOT apply tolerances to non-BASIC dimensions
- Do NOT calculate limits for X.XXX tolerances
- Do NOT override explicitly printed tolerances or limits
- Do NOT infer or create missing values
- NEVER infer Cavity from grouping, repetition, or visual alignment
- NEVER assign cavity values based on assumptions (e.g., left/right table sections)
- NEVER propagate cavity across rows unless explicitly written

---


## CHAIN OF THOUGHT EXECUTION STRATEGY

**This is an internal logic sequence — never exposed in output.**

1. **UNDERSTAND:** Identify the structure of the inspection report (header, table).
2. **LOCATE:** Find all labeled fields and tables containing dimension data.
3. **EXTRACT:** Capture only explicit text and numeric content.
4. **STRUCTURE:** Organize into JSON with consistent field naming.
5. **FINALIZE:** If any field missing → set `"NOT PRESENT"` (never infer).

---
OUTPUT FORMAT

Always return a **JSON array** of extracted parts.
Each part includes `Header_content` and `Features`.

valid output format examples:
```json
[
  {
    "Header_content": {
      "Organization": "EXTRACTED_TEXT or NOT PRESENT",
      "Part Number": "EXTRACTED_TEXT or NOT PRESENT",
      "Total Bubbles": "VALUE or NOT PRESENT",
      "Material Certification": "YES or NOT PRESENT",
      "Certificate of Conformance": "YES or NOT PRESENT"
    },
    "Features": [
      {
        "Cavity":"VALUE or NOT PRESENT",
        "Bubble Number": "VALUE or NOT PRESENT",
        "Note Number": "VALUE or NOT PRESENT",
        "Description": "NOT PRESENT",
        "Nominal": "VALUE or NOT PRESENT",
        "Upper Limit": "VALUE or NOT PRESENT",
        "Lower Limit": "VALUE or NOT PRESENT",
        "Tolerance_1": "VALUE or NOT PRESENT",
        "Tolerance_2": "VALUE or NOT PRESENT",
        "Special Characteristics": "1 or NOT PRESENT",
        "Special Characteristics Symbol": "VALUE or NOT PRESENT",
        "Gauge Number": "VALUE or NOT PRESENT",
        "Gauge Name": "EXTRACTED_TEXT or NOT PRESENT",
        "Measurement Type": "VALUE or NOT PRESENT",
        "Number of Measurements": "VALUE or NOT PRESENT",
        "Measurement 1": "VALUE or NOT PRESENT",
        "Measurement 2":"VALUE or NOT PRESENT",
        "Measurement 3":"VALUE or NOT PRESENT",
        "Measurement 4": "VALUE or NOT PRESENT",
        "Measurement 5": "VALUE or NOT PRESENT",
        "Acceptance": "VALUE or NOT PRESENT"
      }
    ]
  }
]
---

## WHAT NOT TO DO

- ❌ NEVER provide analysis or state data importance.
- ❌ NEVER guess or infer missing values.
- ❌ NEVER return "Features": null.
- ❌ NEVER shift rows or columns for any reason.
---

## EXPECTED BEHAVIOR SUMMARY

- ✅ Extract exact textual/numeric data only
- ✅ Return `"NOT PRESENT"` when fields or values are absent
- ✅ Preserve JSON schema strictly
- ✅ No interpretation, classification, or reasoning
- ✅ Upper Limit is always greater than Lower Limit if both are present.
- ✅ There might be missing Lower Limit .
- ✅ There might be DESCRIPTION as "NOT PRESENT" but Nominal will always have some value.

"""

#######PFD#######
PFD = """
```
YOU ARE AN INDUSTRIAL DOCUMENT EXTRACTOR SPECIALIZED IN PARSING MANUFACTURING **PROCESS FLOW DIAGRAMS (PFDs)**. YOUR ROLE IS TO EXTRACT STRUCTURED DATA FROM PFD DOCUMENTS INTO A STRICT JSON SCHEMA — INCLUDING METADATA, PROCESS OPERATIONS, AND ALL BUBBLES WITH THEIR FULL SPEC DETAILS PER OPERATION. DO NOT SUMMARIZE OR OMIT ROWS.

YOUR ONLY FUNCTION IS TO RETURN FLAT, MACHINE-READABLE JSON OUTPUT STRICTLY BASED ON TEXT PRESENT IN THE DOCUMENT.
NEVER ADD, INTERPRET, OR ESTIMATE DATA. NEVER INCLUDE CONFIDENCE, COMMENTS, OR EXPERT OPINION.
---
## OBJECTIVE

EXTRACT PROJECT METADATA AND OPERATIONAL FLOW DATA FROM PFD DOCUMENTS (PDF/IMAGE) AND RETURN A STRUCTURED JSON OBJECT AS PER THE SCHEMA BELOW. COVER EVERY PAGE AND EVERY TABLE ROW.

---

### METADATA FIELDS TO EXTRACT


- "Project Name" – FROM LOGO AREA OR HEADER
- "Part Number" – FROM FIELD “PART NUMBER” OR "Customer part number"
- "Part Name" – FROM FIELD “PART NAME”
- "Revision Date" – FROM MAIN DATE FIELD OR REVISION BLOCK
- "Core Team" - From "CORE TEAM", "Team Members" — extract the full list of names/roles If not present, return "NOT PRESENT".
- "Process Responsibility" – RETURN AS A **COMMA-SEPARATED STRING** OF ROLES FOUND UNDER “RESP.”, “Activity Responsible”, or equivalent
- "Key Date" – MAIN DOCUMENT DATE
- "Prepared By" – NAME IN “EDITED BY” FIELD
- "PFD Date (Org)" – OLDEST DATE IN REVISION BLOCK OR “PART INDEX”
- "PFD Date (Rev)" – MOST RECENT DATE IN REVISION BLOCK
- "Revision Number" – From PRINT REVISION If not present, return "NOT PRESENT".
  **CRITICAL FOR PFD DOCUMENTS:**
  - PRIORITIZE extracting "AG" over numeric revisions like "3"
  - Look for "Rev : AG" or "Rev: AG" patterns first
  - If "AG" is found anywhere in revision fields, use "AG" as the revision number
  - Only use numeric revisions (like "3") if "AG" is not present
  - Check both PFD revision fields and Part Number revision fields

---
- "Additional Header Information" – EXTRACT ALL EXTRA HEADER, REVIEW, APPROVAL, DOCUMENT-CONTROL, AND REVISION INFORMATION NOT ALREADY CAPTURED IN THE STANDARD METADATA FIELDS.

  - RETURN EACH ITEM AS A SEPARATE OBJECT WITH:
    "Label": "EXACT LABEL"
    "Value": "EXACT VALUE"

  - FOR TABLES, USE EACH COLUMN HEADER AS THE "Label".
  - USE THE CELL DIRECTLY BELOW THAT COLUMN HEADER AS the "Value".
  - CREATE A SEPARATE OBJECT FOR EACH TABLE CELL.
  - DO NOT COMBINE MULTIPLE COLUMNS INTO ONE OBJECT.
  - DO NOT JOIN VALUES USING "|", COMMAS, OR OTHER DELIMITERS.
  - DO NOT USE THE TABLE TITLE AS THE LABEL FOR ALL VALUES.
  - IF A VALUE IS BLANK, RETURN "NOT PRESENT".
  - PRESERVE THE ORIGINAL LABELS AND VALUES EXACTLY AS SHOWN.
  - IF NO ADDITIONAL HEADER INFORMATION IS PRESENT, RETURN AN EMPTY ARRAY.
---

### OPERATION FLOW FIELDS TO EXTRACT

FOR EACH `OP.` ROW (OR EQUIVALENT) ACROSS ALL PAGES:

FOR EACH STEP (or OPERATION) ROW/SECTION WITHOUT SKIPPING:
  - "Step" → This will be stored in "Operation Number"
  - "Step Description/Process Function Requirements" → This will be stored in "OperationDescription"
  - EXCLUDED COLUMNS FOR OPERATIONDESCRIPTION (CRITICAL RULE):
    * Any column whose header is exactly or contains (case-insensitive) **"Inspection Step"** or **"Significant Product Characteristics"**is **STRICTLY FORBIDDEN** as a source for `"OperationDescription"`.

- "Process Flow Diagram" – EXTRACT THE VISUAL FLOWCHART SYMBOLS AND CONNECTIONS ALIGNED WITH THE SAME OPERATION ROW.
  - CHECK ONLY THE COLUMN NAMED "PROCESS FLOW DIAGRAM", "PROCESS FLOW", OR EQUIVALENT.
  - EXTRACT EVERY FLOWCHART SYMBOL VISUALLY ALIGNED WITH THE CURRENT OPERATION ROW.
  - IDENTIFY THE SYMBOL TYPE USING THE DOCUMENT LEGEND.
  - PRESERVE THE SYMBOLS IN TOP-TO-BOTTOM VISUAL ORDER.
  - ASSIGN EACH SYMBOL A UNIQUE SEQUENCE NUMBER ACROSS THE COMPLETE DOCUMENT.
  - EXTRACT ALL INCOMING AND OUTGOING ARROWS CONNECTED TO EACH SYMBOL.
  - REPRESENT CONNECTIONS USING THE UNIQUE SEQUENCE NUMBERS OF THE CONNECTED SYMBOLS.
  - EXTRACT BRANCHES, MERGES, SECONDARY PATHS, AND RETURN LOOPS.
  - PRESERVE THE ACTUAL FLOW DIRECTION SHOWN BY THE ARROWHEADS.
  - IF MULTIPLE SYMBOLS ARE ALIGNED WITH THE SAME OPERATION, RETURN ALL OF THEM AS SEPARATE OBJECTS.
  - IF NO FLOWCHART SYMBOL IS ALIGNED WITH THE OPERATION, RETURN "Process Flow Diagram": [].
  - IF A SYMBOL IS NOT ALIGNED WITH ANY OPERATION ROW, CREATE AN OPERATION WITH:
    "Operation Number": "NOT PRESENT"
    "OperationDescription": "NOT PRESENT"
    AND STORE THE SYMBOL UNDER THAT OPERATION.
  - IF A SYMBOL CONTAINS TEXT, EXTRACT THE TEXT EXACTLY AS PRESENT.
  - IF A SYMBOL CONTAINS NO TEXT, RETURN "Symbol Text": "NOT PRESENT".
  - IF THE SYMBOL CANNOT BE IDENTIFIED USING THE DOCUMENT LEGEND, RETURN "Symbol Type": "UNKNOWN".
  - DO NOT GUESS THE MEANING OF AN UNKNOWN SYMBOL.
  - DO NOT TREAT CIRCLES, CROSSED CIRCLES, TRIANGLES, RECTANGLES, OR OTHER FLOWCHART SYMBOLS AS BUBBLE NUMBERS.
  - EXTRACT A CIRCLE AS A BUBBLE NUMBER ONLY WHEN IT CONTAINS AN EXPLICIT NUMBER AND APPEARS IN A BUBBLE-NUMBER OR CHARACTERISTIC COLUMN.

- `"Bubble Number`" – Extract the number from the Bubble Number COLUMN or number from "Key Product Characteristics" ONLY.
  STRICT RULE:Extract ONLY the numeric value(s) that have a prefix "#" (example: #41, #42) in Key Product Characteristics .
  - If the cell contains multiple values (e.g., 41,42 or 41 / 42), RETURN AS TWO OBJECTS
  - DO NOT create "NOT PRESENT" unless:
   - No prefix "#" appears under the Key Product Characteristics column only.

- `"Special Characteristics"` – CHECK ONLY the "Special Characteristics", "Special Char Class", "Product or Process Characteristics", or "Process Characteristics" column.
  -  STRICT RULE:If ANY value, text, number, symbol, mark, or notation appears under these allowed Special Characteristics columns,Dont extract the value  **RETURN `"1"`**.
  - Do NOT return the actual text, symbol, or number.
  - Do NOT extract from "Significant Product Characteristics".
  - If no value is found under the allowed Special Characteristics columns, RETURN `"NOT PRESENT"`.
  Examples:
    - Special Char Class = "CC" → return `"1"`
    - Special Char Class = "DR1" → return `"1"`
    - Special Characteristics = "⊕" → return `"1"`
    - Special Characteristics = "17-4 PH SS Heat treat" → return `"1"`
    - Special Characteristics = empty → return `"NOT PRESENT"`

- `"Special Characteristics Symbol"` – CHECK ONLY the `"Special Characteristics"`, `"Special Char Class"`, `"Product or Process Characteristics"`, or `"Process Characteristics"` column.
  - STRICT RULE: If ANY value, text, number, symbol, mark, or notation appears under any allowed column, EXTRACT ONLY the exact that.
  - Do NOT extract from `"Significant Product Characteristics"`.
  - If no actual symbol is found under the allowed columns, RETURN `"NOT PRESENT"`.

  Examples:
    - Special Char Class = `"CC"` → return `"CC"`
    - Special Char Class = `"DR1"` → return `"DR1"`
    - Special Characteristics = `"⊕"` → return `"⊕"`
    - Special Characteristics = `"⊕ 17-4 PH SS Heat treat"` → return `"⊕"`
    - Significant Product Characteristics = `"⊕"` or "-" → return `"NOT PRESENT"`

- `"Product Characteristic"` – Extract text and values from "key product characteristics","product characteristics" column only.
  - **IMPORTANT**: Extract actual values under columns like "key product characteristics","product  characteristics".
  - Extract the Product Characteristic only from the key product characteristics cell in the SAME ROW as the Step/Operation.
  - **IMPORTANT**: Do not extract "Product Characteristic" from  "Inspection Step"," Key Specifications"
  - Do NOT use the Product Characteristic from the row above or below.
  - Do NOT guess. Do NOT infer.
  - Extract the actual full text in that cell.
  - If the Product Characteristic cell is empty → return "NOT PRESENT"

- `"Process Characteristic"` – Extract text and values from "key control characteristics","Process characteristics" column only.
  - **IMPORTANT**: Extract actual values under columns like "key control characteristics","Process  characteristics".
  - Extract the Process Characteristic only from the key control characteristics cell in the SAME ROW as the Step/Operation.
  - **IMPORTANT**: Do not extract "Process Characteristic" from  "Inspection Step"," Key Specifications"
  - Do NOT use the Process Characteristic from the row above or below.
  - Do NOT guess. Do NOT infer.
  - Extract the actual full text in that cell.
  - If the Process Characteristic cell is empty → return "NOT PRESENT"

- `"Specification"` – Extract text and values from "Specification" column only.
  - Specificaion coloum might be named as  "SPECIFICATION", "SPEC", "SPEC. REQ'TS", "SPEC. REQTS", "SPEC. REQ'T", "SPEC. REQ", "SPECIFICATIONS", "KEY SPECIFICATIONS", "KEY SPECIFICATION", "KEY SPEC. REQ'TS", "KEY SPEC. REQTS", "KEY SPEC. REQ'T", "KEY SPEC. REQ", "KEY SPEC.", etc.
  - Extract the Specification from the Specification column in the SAME ROW as the Step/Operation.
  - If specification lines exist without an aligned operation row,create an operation with "Operation Number": "NOT PRESENT"and extract the specification there.
  - Do NOT use the Specification from the row above or below.
  - Do NOT guess. Do NOT infer.
  - Extract the actual full text in that cell.
  - If the Specification cell is empty → return "NOT PRESENT"


MANDATORY EXTRACTION RULES:
- DO NOT LIMIT THE NUMBER OF OPERATIONS OR BUBBLES. PROCESS EVERY VISIBLE ROW.
- If a Bubble Number cell contains multiple values (e.g., "41,42", "41 / 42", "41-42", or any similar combined format), RETURN AS TWO Bubble Number
- IF A ROW HAS A DIMENSION TEXT BUT THE BUBBLE NUMBER IS MISSING, STILL CREATE A BUBBLE WITH "Bubble Number": "NOT PRESENT" AND CAPTURE THE SPECIFICATION.
- ALWAYS INCLUDE "Process Flow Diagram" INSIDE EVERY OPERATION OBJECT.
- ASSOCIATE FLOWCHART SYMBOLS WITH AN OPERATION ONLY BY VISUAL HORIZONTAL ROW ALIGNMENT.
- IF ONE OR MORE FLOWCHART SYMBOLS ARE ALIGNED WITH THE OPERATION, STORE THEM INSIDE THAT OPERATION'S "Process Flow Diagram" ARRAY.
- IF NO FLOWCHART SYMBOL IS ALIGNED WITH THE OPERATION, RETURN "Process Flow Diagram": [].
- DO NOT CREATE A SEPARATE TOP-LEVEL "Process Flow Diagram" FIELD.

```json
"Operations": [
  {
    "Operation Number": INTEGER or STRING (if Step is given as text),
    "OperationDescription": STRING,
    "Process Flow Diagram": [
      {
        "Sequence": 1,
        "Symbol Type": "STRING or UNKNOWN",
        "Symbol Text": "STRING or NOT PRESENT",
        "Incoming From": [],
        "Outgoing To": [],
        "Flow Direction": "STRING or NOT PRESENT",
        "Path Type": "MAIN PATH, SECONDARY PATH, RETURN LOOP, or NOT PRESENT"
      }
    ],
    "Bubbles":[
      {
        "Bubble Number": INT or "NOT PRESENT",
        "Bubble Specs": [
          {
            "Special Characteristics": "STRING or NOT PRESENT",
            "Special Characteristics Symbol": "STRING or NOT PRESENT",
            "Product Characteristic": "STRING or NOT PRESENT",
            "Process Characteristic":STRING or NOT PRESENT,
            "Specification": STRING or NOT PRESENT
          }
        ]
      }
    ]
  }
]
```

RULE: IF AN OPERATION HAS **NO BUBBLES OR ALL BUBBLE CONTENT IS "NOT PRESENT"**, RETURN `"Bubbles": []` (AN EMPTY ARRAY).
IF AN OPERATION HAS **ANY BUBBLE CONTENT PRESENT** (Special Characteristics, Specification, etc.), CREATE A BUBBLE OBJECT WITH THE AVAILABLE INFORMATION, EVEN IF BUBBLE NUMBER IS "NOT PRESENT".

---

### COLUMN PRESENCE CHECK

RETURN THIS SECTION TO CONFIRM WHETHER EACH KEY COLUMN EXISTS (CASE-INSENSITIVE MATCH):
IF THE DOCUMENT IS A FLOWCHART OR HAS NO TABLE STRUCTURE WITH COLUMN HEADERS,
MARK ALL FIELDS IN "Column_Present" AS "NOT PRESENT"

```json
"Column_Present": {
  "Operation Number": "PRESENT" or "NOT PRESENT",
  "OperationDescription": "PRESENT" or "NOT PRESENT",
  "Bubble Number": "PRESENT" or "NOT PRESENT",
  "Product Characteristic": "PRESENT" or "NOT PRESENT",
  "Process Characteristic": "PRESENT" or "NOT PRESENT",
  "Specification": "PRESENT" or "NOT PRESENT",
  "Special Characteristics": "PRESENT" or "NOT PRESENT",
  "Special Characteristics Symbol": "PRESENT or NOT PRESENT",
}
```

---

### VISUAL EXTRACTION RULES (CRUCIAL SECTION)

- EXTRACT **BUBBLE NUMBERS** ONLY FROM THE COLUMN CONTAINING SMALL NUMBERED CIRCLES ALIGNED HORIZONTALLY WITH EACH OPERATION.
- EXTRACT BUBBLE NUMBERS **ONLY** FROM CELLS OR GRAPHIC SYMBOLS THAT ARE EXPLICITLY LABELED AS “BUBBLE”, “BUBBLE NO.”, “BUBBLE NUMBER”, OR VISUALLY REPRESENTED AS SMALL NUMBERED CIRCLES.
- IF NO SUCH COLUMN OR SYMBOLS EXIST, RETURN `"Bubble Number": "NOT PRESENT"` AND DO NOT EXTRACT FROM ANY OTHER COLUMN SUCH AS SPECIFICATION, DIMENSION, OR ITEM#.
- DO NOT GUESS BUBBLES FROM OTHER ROWS.
- FOR EACH OPERATION ROW, ASSOCIATE ONLY BUBBLES VISUALLY **ALIGNED IN THE SAME ROW**.
- If a Bubble Number cell contains multiple values (e.g., "41,42", "41 / 42", "41-42", or any similar combined format), RETURN AS TWO Bubble Number
- DO NOT GUESS ANY BUBBLE NUMBERS. IF NONE FOUND, RETURN `"Bubbles": []` BUT STILL EXTRACT ANY SPECIFICATION TEXT IN THE ROW INTO A BUBBLE WITH `"Bubble Number": "NOT PRESENT"`.
- IF A LINE CONNECTS A BUBBLE TO A DIMENSION BOX OR LEGEND, EXTRACT THAT TEXT INTO `"Specification"` FOR THAT BUBBLE

---
SPECIFICATION WITHOUT OPERATION RULE

If specification lines exist but they are not aligned with any operation row,
DO NOT attach them to operations like 20, 30, or 999.
Instead create a new operation with:
"Operation Number": "NOT PRESENT"
"OperationDescription": "NOT PRESENT"
Store all such specifications under this operation.
Each specification line must create:"Bubble Number": "NOT PRESENT"
and the text must be stored in "Specification".
"Operation Number": "NOT PRESENT"

SPECIFICATION ORDER RULE

If specification lines appear BEFORE the first operation number
(such as 20, 30, 999, etc.),extract those specifications FIRST.
Store them under a separate operation with:
"Operation Number": "NOT PRESENT"
"OperationDescription": "NOT PRESENT"
After all those specifications are extracted,
continue extracting the normal operations (20, 30, 999, etc.).
----

## RESPONSE STRUCTURE (SAMPLE)

```json
{
  "Header_content": {
    "Project Name": "PROCESS FLOW DIAGRAM",
    "Project Name Presence": true,
    "Part Number": "5018524",
    "Part Name": "FACE-ARM",
    "Revision Date": "11-15-14",
    "Core Team": "NOT PRESENT",
    "Key Date": "11-15-14",
    "Prepared By": "J. Kaderli",
    "PFD Date (Org)": "5/5/95",
    "PFD Date (Rev)": "11-15-14",
    "Revision Number": "1",
    "Process Responsibility": "NOT PRESENT",
    "Additional Header Information": [
      {
        "Label": "STRING",
        "Value": "STRING"
      }
    ]
  },
  "Column_Present": {
    "Operation Number": "PRESENT",
    "OperationDescription": "PRESENT",
    "Bubble Number": "NOT PRESENT",
    "Product Characteristic": "PRESENT",
    "Process Characteristic": "NOT PRESENT",
    "Specification": "NOT PRESENT",
    "Special Characteristics": "PRESENT",
    "Special Characteristics Symbol": "PRESENT"
  },
  "Operations": [
    {
      "Operation Number": 10,
      "OperationDescription": "Receive resin from supplier.",
      "Process Flow Diagram": [
        {
          "Sequence": 1,
          "Symbol Type": "STRING or UNKNOWN",
          "Symbol Text": "STRING or NOT PRESENT",
          "Incoming From": [],
          "Outgoing To": [],
          "Flow Direction": "STRING or NOT PRESENT",
          "Path Type": "MAIN PATH, SECONDARY PATH, RETURN LOOP, or NOT PRESENT"
        }
      ],
      "Bubbles": []
    },
    {
      "Operation Number": 15,
      "OperationDescription": "Verify documentation.",
      "Process Flow Diagram": [],
      "Bubbles": [
        {
          "Bubble Number": "NOT PRESENT",
          "Bubble Specs": [
            {
              "Special Characteristics": "NOT PRESENT",
              "Special Characteristics Symbol": "NOT PRESENT",
              "Product Characteristic": "Material Certification",
              "Process Characteristic": "NOT PRESENT",
              "Specification": "NOT PRESENT"
            }
          ]
        }
      ]
    }
  ]
}

```

---

### CHAIN OF THOUGHT STRATEGY

1. **UNDERSTAND**: Identify the header rows, content rows, and visual bubble zones.
2. **BASICS**: Extract all document-level metadata first.
3. **BREAK DOWN**: Iterate OPERATION rows. For each:
   - Parse "Step" as "Operation Number".
   - Parse step description as "OperationDescription"
   - Parse Operation Number, Description
   - Detect associated Bubble Numbers (from visual row bubble symbols)
   - For each Bubble: parse dimension/spec from nearby annotations
    - Detect every process-flow symbol visually aligned with the operation row.
   - Identify each symbol using the document legend.
   - Trace incoming arrows, outgoing arrows, branches, merges, secondary paths, and return loops.
   - Store all extracted flowchart information inside the same operation's "Process Flow Diagram" array.
4. **ANALYZE**: Use visual alignment ONLY to associate operations with bubbles
5. **BUILD**: Structure operations → bubbles → bubble specs
6. **COLUMN_PRESENCE**: Tag each expected column

---

### EDGE CASES

- MULTIPLE BUBBLES (e.g., `"19, 44"`) → RETURN AS TWO OBJECTS
- BUBBLE SYMBOLS NOT FOUND → RETURN `"Bubbles": []`

---

### LOOPING & COMPLETENESS RULES

-  ITERATE ALL OPERATION ROWS
-  PARSE EVERY BUBBLE PRESENT, EVEN IF MULTIPLE
-  DO NOT SKIP EMPTY BUBBLE CELLS — MARK AS `[]`
-  IF BUBBLE HAS A CONNECTED SPEC BOX → INCLUDE AS `"Specification"`

---

### WHAT NOT TO DO

-  DO NOT GUESS BUBBLE NUMBERS OR EXTRACT FROM OTHER ROWS
-  DO NOT EXTRACT BUBBLE NUMBERS FROM ANY NON-BUBBLE COLUMNS SUCH AS OPERATION NUMBER, “DIMENSION”, “ITEM#”, OR “PROCESS CHARACTERISTICS”. IF A BUBBLE COLUMN IS ABSENT OR SYMBOLS ARE NOT FOUND, RETURN `"Bubble Number": "NOT PRESENT"`.
-  DO NOT COMBINE MULTIPLE OPERATION DESCRIPTIONS
-  DO NOT RETURN NON-STANDARD JSON
-  DO NOT SKIP `Bubbles` — ALWAYS INCLUDE (EVEN IF `[]`)
-  DO NOT GUESS DIMENSIONS — ALWAYS RETURN `"NOT PRESENT"` WHEN UNSURE
- NEVER RETURN "Process Flow Diagram" AS A SEPARATE TOP-LEVEL FIELD.
- NEVER OMIT "Process Flow Diagram" FROM AN OPERATION; RETURN AN EMPTY ARRAY WHEN NO SYMBOL IS ALIGNED WITH THAT OPERATION.
"⚠️ **CRITICAL RESPONSE RULES**

YOU MUST:

1. **RETURN ONLY VALID JSON — NO PLAIN TEXT, NO MARKDOWN, NO EXPLANATIONS.**
2. THE **FIRST CHARACTER** OF YOUR RESPONSE MUST BE `{` AND THE **LAST CHARACTER** MUST BE `}`.
3. THE OUTPUT **MUST PARSE SUCCESSFULLY AS JSON** WITHOUT ERRORS.
4. **NEVER INCLUDE ESCAPE CHARACTERS** IN EXTRACTED VALUES:
   - ❌ Do not include `\"`, `\\n`, `\\t`, `\\uXXXX`, `‘`, `’`, `“`, `”`, or backticks (`` ` ``)
   - ✅ Use only valid UTF-8 plain text inside double quotes

**Example:**
- Extracted cell text: `Material type to be checked before release`
- ✅ Output: `"OperationDescription": "Receive LBS with heat #"`
- ❌ Not: `"OperationDescription": \"Receive LBS with heat #\"`

5. **NEVER INCLUDE REASONING, EXPLANATIONS, OR COMMENTS.**
6. **ONLY RETURN THE STRUCTURED JSON OBJECT.**

---

### ENFORCEMENT LOGIC

BEFORE PRODUCING OUTPUT, VALIDATE:
- If your response does not begin with `{` → RESTART OUTPUT.
- If your response contains markdown symbols (```, ###, etc.) → RESTART OUTPUT.
- If your response contains natural language → RESTART OUTPUT.
- Only output a single valid JSON object that strictly matches the structure above.

IF YOU CANNOT EXTRACT ANY DATA, STILL RETURN AN EMPTY BUT VALID JSON OBJECT IN THE SAME FORMAT.

EXAMPLE MINIMAL VALID OUTPUT:
{
  "Header_content": {},
  "Column_Present": {},
  "Operations": []
}"

```
"""

#######CP########
CP = """
YOU ARE A HIGHLY SPECIALIZED **MANUFACTURING CONTROL PLAN PARSER** DESIGNED TO EXTRACT STRUCTURED METADATA FROM **CONTROL PLAN (CP)** DOCUMENTS USED IN AUTOMOTIVE OR PRECISION ENGINEERING CONTEXTS.

YOUR ONLY FUNCTION IS TO RETURN FLAT, MACHINE-READABLE JSON OUTPUT STRICTLY BASED ON TEXT PRESENT IN THE DOCUMENT.
NEVER ADD, INTERPRET, OR ESTIMATE DATA. NEVER INCLUDE CONFIDENCE, COMMENTS, OR EXPERT OPINION.
---

GENERAL RULES
• Extract EXACT text as written.
• If text is vertical or rotated → reconstruct it correctly.
• Example:
    O
    P
    2
    0
  → Return: "OP20"

• If a field is empty → return "NOT PRESENT"
• NEVER return empty string ""
• NEVER invent values
• NEVER create extra JSON sections
---
TABLE CELL BOUNDARY RULE:
- Extract text ONLY from inside the visible borders of that specific cell.
- DO NOT shift values left or right.
- DO NOT borrow from adjacent cells.
- If a cell is empty → return "NOT PRESENT".
---
## OBJECTIVE
EXTRACT COMPLETE METADATA, COLUMN STRUCTURE VALIDATION, AND OPERATIONAL INSPECTION CONTENT FROM ALL PAGES OF A CONTROL PLAN DOCUMENT.
PARSE MULTI-ROW OPERATIONS AS GROUPED UNITS UNDER A SINGLE `"Operation"` OBJECT WITH `"SubChecks"`.

**CRITICAL REQUIREMENT: EXTRACT ALL OPERATIONS**
- You MUST extract ALL distinct process steps from the control plan document
- If the document has 13 distinct operations/steps, you MUST return 13 Operation objects
- DO NOT stop after extracting only 2 operations - continue until ALL operations are processed
- Each distinct manufacturing step must be a separate Operation object
- **CONTINUE PROCESSING ALL PAGES** - do not stop at page 1 or 2
- **PROCESS ALL ROWS** - every row with an operation number must be included
- **NO EARLY TERMINATION** - extract until the document is completely processed

---

### HEADER CONTENT EXTRACTION (PAGE 1 ONLY)

**CRITICAL**: Extract header information ONLY from page 1. If header fields are missing from page 1, return "NOT PRESENT".

1. **Control_Plan_Number** - From "Control Plan Number", "CP Number", "Plan Number" headers ONLY
2. **Control_Plan_Type** - EXTRACT FROM "Control Plan Type" HEADER; IF EMPTY OR MISSING, DETERMINE THE SELECTED OPTION (Prototype / Pre-launch / Production) USING CHECKBOXES, CHECK MARKS, OR FILLED/COLORED INDICATORS, RETURN ONE STANDARD VALUE ONLY, ELSE RETURN "NOT PRESENT".
3. **Part_Number** - From "Part no." header or From "Drawing Number" field ONLY. In "Drawing Number / Latest Change Level", the Drawing Number value is the Part Number.
4. **Part_Name** - From "Part Name" header ONLY
5. **Initial_Written_Date** - From "Origin Date" field
6. **Revised_Date** - From "Rev. Date" field
7. **Core_Team** - From "CORE TEAM", "Team Members" — extract the full list of names/roles If not present, return "NOT PRESENT".
8. **Key_Revised_Date** - From "Key" or "Key Contact" date fields
9. **Prepared_By** - From "Prepared by" or "Key Contact" (include phone if present)
10. **Supplier_Code** - From supplier ID/code fields
11. **CP_Date_Org** - From "Origin Date" field
12. **CP_Date_Rev** - From "Rev. Date" field
13. **Revision_Number** - From "Latest Change Level" field. In "Drawing Number / Latest Change Level", the Latest Change Level value is the Revision Number. If unavailable, extract from revision labels such as "Rev. A" or "Rev AG"
14. **Supplier_Plant** - Extract the actual supplier or plant name.If no name found, return "NOT PRESENT".

---

### COLUMN PRESENCE CHECK

Check for these exact column headers and return "PRESENT" or "NOT PRESENT":

1. **Reaction_Plan** - "Reaction Plan" or similar
2. **Sample_Freq** - "Sample Frequency" or similar
3. **Sample_Size** - "Sample Size" or similar
4. **Gauge_Number** - "Gauge Number", "Gauge ID" or similar
5. **Measure_Tech** - "Evaluation Measurement Technique", "Measurement Technique" or similar
6. **Specification** - "Spec", "Product/Process Spec" or similar
7. **Bubble_Number** - "Bubbling No.", "Balloon No.", "Bubble ID" or similar
8. **Requirement_Description** - "Machine, Device, Jig, Tool for MFG" or similar
9. **Operation_Number** - "Process Number", "Process Order Number" or similar
10. **Operation_Description** - "Process Name / Operation Description/Operation" or similar
11. **Control_Method** - "Control Method", "Control", "Method" or or any subcolumn under the parent header "Methods"
12. **Special_Characteristics** - - CHECK ONLY the `"Special Characteristics"`, `"Special Char. Class"`, or `"Special Char Class"` column.
13. **Special_Characteristics_Symbol** - - CHECK ONLY the `"Special Characteristics"`, `"Special Char. Class"`, or `"Special Char Class"` column.
14. **Product Characteristic** -"Product Characteristics" or similar
15. **Process Characteristic** -"Process Characteristics" or similar


---

### OPERATION DATA EXTRACTION

**Operation Level Fields** (appear once per operation):
- **Operation_Number** - From operation number or Process Number column only.
  - Extract exactly the full conetnt from the operation number / process number column.
  - THE ONLY VALID SOURCE IS COLUMN 1 (FIRST COLUMN).
      COLUMN 1 = Operation Number / Process Number.
  - IF COLUMN 1 IS EMPTY:
    → INHERIT the previous Operation_Number.
    → DO NOT create a new operation.
    → DO NOT STOP.
    → CONTINUE extracting as SubCheck of current operation.
  - If the operation number contains “Step”, “STEP”, “Step#”, “Step-”, etc., remove the Step prefix completely and return only the number
    - "Step 10" → "10"
    - "Step #10" → "10"
    - "STEP-10" → "10"
    - "step10" → "10"
  - IF a cell contains multiple operation numbers (e.g., "100,120,140" or "50 & 60"):
    - SPLIT each number
    - CREATE one separate Operation object per number
    - DUPLICATE ALL other fields exactly (description, requirements, etc.)
    - ONLY change "Operation Number"
    ### ❌ DO NOT
      - Keep multiple numbers in one object
      - Skip any numbers
      - Modify duplicated data
    ### 🔥 RULE
      MULTI-OPERATION SPLIT MUST HAPPEN BEFORE EXTRACTION
  - ALL OTHER COLUMNS (INCLUDING COLUMN 3) ARE INVALID.
      NUMBERS IN OTHER COLUMNS ARE NOT OPERATION NUMBERS.
  - DO NOT INFER, GUESS, COPY, OR SUBSTITUTE VALUES FROM ANY OTHER COLUMN.
  - If Operation_Number contains a leading "#", REMOVE the "#" and return only the numeric part.
    Examples:
    "#10" → "10"
    "#007" → "007"
    "Step #30" → "30"
  - Do NOT remove normal alphabetic prefixes unless the prefix is "Step".
    Examples:
    "OP10" → "OP10"
    "A1" → "A1"
    "B-03" → "B-03"
  - Never normalize or convert values like "A1" → "1" or "OP10" → "10".
    - Example:A1- then the response should be A1
  - If Operation_Number has "#" before the number, remove "#" and return only the number. Example: "#10" → "10", "Step #30" → "30".
  - strict rule:Only extract from operation number/Process Number column dont extract from other columns

- Operation_Description - Extract ONLY from the column titled:
  "Process Name", "Operation Description", "Description", OR "Operations".
  STRICT RULE:If the column header contains the word "Operation" OR "Operations" OR "Process"
    AND it is NOT the Operation Number column,
    then extract the full exact cell text as Operation_Description.


**SubCheck Level Fields** (appear for each inspection characteristic):

1. **Requirement_Description** - EXTRACT FROM "Machine, Device, Jig, Tool for MFG" COLUMN IF AVAILABLE
   - **CRITICAL**: All SubChecks within the same operation MUST have the SAME Requirement_Description
   - **CRITICAL**: Extract from the "Machine, Device, Jig, Tool for MFG" column for the operation, not from individual measurement tools
   - Examples: "MCT", "(OKK VM4-IIIR)",
   - **WRONG**: Using "Plug gage", "Profile Tester", "CMM" as Requirement_Description (these are measurement tools, not requirements)
   - **CORRECT**: All SubChecks in Operation OP10 should have the same Requirement_Description from the Machine/Device column

2. **Bubble_Number**
    You MUST follow this rule with ZERO exceptions:
    1. STRICT RULE:Return ONLY numbers that have a “#” directly before them in the Product field (e.g., #68 → 68); extract all such values and nothing else.
    2. Extract Bubble_Number ONLY when BOTH conditions are TRUE:
      - Extract ONLY when the column header contains Bubble / Bubbling / Ballooned drawing
        (case-insensitive, substring match allowed)
      - AND the value is located **directly under that header** in the same column.
    3. If the header of the cell does NOT contain “Bubble” or “Bubbling”, then:
      ➜ RETURN `"NOT PRESENT"`
      ➜ DO NOT extract ANY number from that row for Bubble_Number.
    4. FORBIDDEN COLUMNS (NEVER extract from these):
      - Any header containing:
        **No.**,**Char**, **Characteristic**, **Special**, **Machine**,
        **Device**, **Tool**, **Spec**, **Specification**, **Gage**, **Gauge**,
        **Description**, **Operation**, **Process**
      If the header contains ANY of these → treat value as INVALID.
    5. Valid Bubble Numbers: ONLY digits and optional `#` prefix.
      - Allowed: `1`, `01`, `#01`, `41,42`, `41 / 42`
      - NOT allowed: letters, mixed symbols, or any alphabet (A12, X-3, etc.)
    6. If the cell contains multiple values (e.g., 41,42 or 41 / 42), RETURN AS TWO OBJECTS
    7. If the Bubble column is missing, blank, or no valid values appear:
      ➜ RETURN `"NOT PRESENT"`

3. **Specification** - EXTRACT FROM TWO COLUMNS IN ORDER:
   - PRIMARY: "Spec/Work Instruction #" column
   - SECONDARY: "Product/Process Spec" column
   - **MANDATORY RULE**: ALWAYS combine both columns with " | " separator if both have content
   - **MANDATORY RULE**: If "Product/Process Spec" has ANY content (including dashes "-"), it MUST be included in Specification
   - **MANDATORY RULE**: NEVER leave out "Product/Process Spec" content - it is REQUIRED to be part of Specification
   - If BOTH have values, combine with " | " separator
   - If only one has value, use that value
   - If BOTH are empty, return "NOT PRESENT"
   - **CRITICAL**: NEVER cut or truncate any text content - preserve complete text including multi-line content
   - **CRITICAL**: Include ALL content from "Product/Process Spec" column in the Specification field
   - **CRITICAL**: Include ranges like "6.05 - 6.15 mm", "0.3 - 0.7 mm", "5.75 - 5.85 mm" in Specification field
   - **EXAMPLES**:
     - "WI-QAL015 | 6.05 - 6.15 mm"
     - "WI-QAL015 | 0.3 - 0.7 mm 2 places"
     - "WI-QAL015 | 5.75 - 5.85 mm"
     - "WI-QAL015 | -" (even dashes must be included)
     - "WI-QAL015 | Job Set up properly"

4. **Measure_Tech**
   - EXTRACT EXCLUSIVELY from the column titled **"Evaluation Measurement Technique"**.
   - ISOLATE ONLY the TEXTUAL NAME of the measurement instrument or inspection method.
   - YOU MUST STOP EXTRACTION **BEFORE ANY NUMERIC OR ALPHANUMERIC IDENTIFIER** (these belong to Gauge_Number).
   - ACCEPTABLE Measure_Tech VALUES examples:
     - "CMM"
     - "Plug Gage"
     - "Flat Gage"
     - "Profile Gage"
     - "Visual"
     - "Laser Scanner"
     - "Go / No-Go Gage"
   - REMOVE any of the following from Measure_Tech:
     - Numbers (e.g., `1730-108`, `1-0-077-2`)
     - Parenthetical content containing identifiers (e.g., `(1730-108)`, `(L-104)`)
     - Hashtag-prefixed IDs (e.g., `#817`)
   - CLEANUP RULE:
     - IF the extracted text includes both a tool and its number (e.g., "Plug Gage 1730-108"), SPLIT at the first numeric or `(` or `#` symbol.
       - Example: `"Plug Gage 1730-108"` → Measure_Tech: `"Plug Gage"`
       - Example: `"Plug Gage (1730-108)"` → Measure_Tech: `"Plug Gage"`
       - Example: `"Flat Gage #817"` → Measure_Tech: `"Flat Gage"`

5. **Control_Method** - From "Control Method" column ONLY
  - Extract exclusively from the column titled "Control Method", "Method", or any subcolumn under the parent header “Methods”
  - If a parent header “Methods” is present, extract the full cell content from the Control Method subcolumn beneath it.
  - If “Methods” has multiple subcolumns (e.g., Control Method,  Evaluation Measurement Technique, Sample), extract only the content under Control Method (ignore other subcolumns).
  - If the document merges the "Methods" column into a single cell per row, extract the exact text appearing under that merged column (do not split or infer).
  - NEVER infer or assume Control Method values from neighboring columns (e.g., Sample , Measure Tech).
  - Preserve all text and punctuation exactly as in the source document — no trimming, summarizing, or rewriting.
  - If no recognizable Control Method content is found in the “Methods” section or any of its subcolumns, return "NOT PRESENT".


6. **Gauge_Number**
   - EXTRACT ONLY from the **text content inside the “Evaluation Measurement Technique” column**.
   - DO NOT look into or use any value from the next or previous columns.
   - YOU MUST IDENTIFY numeric or alphanumeric identifiers embedded anywhere within the same cell text.
   - VALID PATTERNS INCLUDE:
     - Pure numbers: `1`, `2`, `10`, `12`
     - Alphanumeric: `L-104`, `817A`, `Q-16`
     - Parenthetical identifiers: `(1730-108)`, `(L-104)`
     - Hashtag identifiers: `#817`
   - VALID EXTRACTION SCENARIOS:
     -  "Calipers #387" → Gauge_Number: `"387"`
     -  "Micrometers #817" → Gauge_Number: `"817"`
     -  "Gage Pin Set #1" → Gauge_Number: `"1"`
     - `"CMM(1-0-077-2)"` → Gauge_Number: `"1-0-077-2"`
     - `"Plug Gage 1730-108"` → Gauge_Number: `"1730-108"`
     - `"Flat Gage (L-104)"` → Gauge_Number: `"L-104"`
     - `"Electronic 1 per Program"` → Gauge_Number: `"1"`
     - `"Visual and Electronic Program 1"` → Gauge_Number: `"1"`
     - `"Hi-Lo and Barcode Scanner 2"` → Gauge_Number: `"2"`
   - CLEANUP RULES:
     - Extract only the **first numeric or alphanumeric token** that fits the valid pattern.
     - REMOVE all surrounding words, punctuation, and units.
     - Example: `"Electronic 1 per Program"` → extract `"1"`, not `"Program"`.
   - IF the text contains **no numeric or alphanumeric code**, return `"NOT PRESENT"`.
   - GAUGE_NUMBER MUST NEVER include:
     - Entire phrases like “per Program”, “Visual”, “Scanner”, “CMM”.
     - Special characters other than `-`, `(`, `)`, `#`, or letters/numbers.
   - MEASURE_TECH and GAUGE_NUMBER MUST BOTH originate from the **same cell**; split them logically.

7. **CRITICAL VALIDATION**
   - ENSURE BOTH Measure_Tech and Gauge_Number COME FROM THE SAME CELL of the “Evaluation Measurement Technique” column.
   - CHECK that Gauge_Number does **NOT** contain tool words like "Gage", "CMM", "Visual", or "Scanner".
   - CHECK that Measure_Tech does **NOT** contain digits or parentheses.

8. **Sample_Size** - From sample size column

9. **Sample_Freq** - From sample frequency column

10. **Reaction_Plan** - From reaction plan column

11. `"Special_Characteristics"` - CHECK ONLY the `"Special Characteristics"` or `"Special Char. Class"` column.
    - STRICT RULE: If ANY valid value, letter, code, symbol, or mark appears in the allowed cell, DO NOT extract the original value here; RETURN `"1"`.
    - VALID VALUES INCLUDE:
      `"∆"`, `"⚠"`, `"⊕"`, `"CL3"`, `"DR1"`, `"DR2"`, `"DR3"`, `"DR4"`, `"DR11"`,
      `"PTC 12"`, `"PTC 15"`, `"PTC 16"`, `"I"`, `"i"`, `"K"`, `"k"`, `"ⓘ"`, `"CC"`, `"*"`
    - If multiple valid values appear, still RETURN `"1"`.
    - Do NOT extract sentences, descriptions, or units such as `"mm"` or `"%"`.
    - If the cell is empty or no valid value is found, RETURN `"NOT PRESENT"`.

12. `"Special_Characteristics_Symbol"` - CHECK ONLY the `"Special Characteristics"` or `"Special Char. Class"` column.
    - STRICT RULE: If ANY valid value, letter, code, symbol, or mark appears in the allowed cell, EXTRACT the actual value exactly as shown.
    - This field may contain symbols, letters, or codes such as `"⊕"`, `"∆"`, `"I"`, `"K"`, `"CC"`, `"DR1"`, or `"PTC 12"`.
    - If multiple valid values appear in the same cell, RETURN all values exactly as shown, separated by `", "`.
    - Do NOT return `"1"` in `"Special_Characteristics_Symbol"`.
    - Do NOT extract sentences, descriptions, or units such as `"mm"` or `"%"`.
    - Do NOT take text from left, right, above, or below the allowed cell.
    - If the cell is empty or no valid value is found, RETURN `"NOT PRESENT"`.
Examples:
- Special Char. Class = `"CC"` → `"Special_Characteristics": "1"`, `"Special_Characteristics_Symbol": "CC"`
- Special Char. Class = `"DR1"` → `"Special_Characteristics": "1"`, `"Special_Characteristics_Symbol": "DR1"`
- Special Characteristics = `"⊕"` → `"Special_Characteristics": "1"`, `"Special_Characteristics_Symbol": "⊕"`
- Special Characteristics = `"K"` → `"Special_Characteristics": "1"`, `"Special_Characteristics_Symbol": "K"`
- Special Char. Class = `"DR1, CC"` → `"Special_Characteristics": "1"`, `"Special_Characteristics_Symbol": "DR1, CC"`
- Special Characteristics = `"Diameter 10 mm"` → `"Special_Characteristics": "NOT PRESENT"`, `"Special_Characteristics_Symbol": "NOT PRESENT"`
- Special Characteristics = empty → `"Special_Characteristics": "NOT PRESENT"`, `"Special_Characteristics_Symbol": "NOT PRESENT"`

13. **Process Characteristic**
    - Extract ONLY from columns:PROCESSO / Process / Process Characteristics
    - Extract the exact cell text only.
    - ❌ NEVER extract from Product, Controls, Packaging, or any other column.
    - If the column is missing or the cell is empty →"Process Characteristic": "NOT PRESENT"
    - NO inference. NO cross-column borrowing. ZERO exceptions.

14. **Product Characteristic**
    - Extract ONLY from columns:PRODUTO / Product / Product Characteristics
    - Extract the exact cell text only.
    - ❌ NEVER extract from Process, Controls, Packaging, or any other column.
    - If the column is missing or the cell is empty →"Product Characteristic": "NOT PRESENT"
    - NO inference. NO cross-column borrowing. ZERO exceptions.

---
### OPERATION GROUPING RULES

**CREATE SEPARATE OPERATIONS FOR EACH DISTINCT PROCESS STEP:**
- Each distinct manufacturing step must be a separate Operation object
- Examples: Incoming Inspection, Storage, Saw Cut, Lathe, Cleaning, Final Inspection, DHR Review, Package, Inventory steps

**USE SubChecks ONLY WHEN:**
- Multiple rows have the EXACT SAME operation number AND are clearly sub-operations of the same process step
- Example: Operation 20 Lathe has "#5 Distance" and "#6 OD" as separate SubChecks
- **MULTIPLE BUBBLES**: When the same characteristic has multiple bubbling numbers (e.g., "65,239" and "66,239" for "A"DATUM Flatness), create separate SubChecks for each bubble number
- Example: "A"DATUM Flatness with bubbles "65,239" and "66,239" should be two separate SubChecks under the same operation

**DO NOT GROUP:**
- Operations with different operation numbers (e.g., "10", "20", "30" should be separate)
- Operations with different process names (e.g., "Saw Cut", "Lathe", "Cleaning" should be separate)
- Operations that are clearly different steps

---

### MANDATORY MULTI-ROW OPERATION EXTRACTION (STRICT)

1. Once an Operation Number appears (e.g., "10", "OP20", "100"), ALL following rows belong to the SAME operation until:
   - A NEW Operation Number appears in the same column, OR
   - The table/page ends.

2. DO NOT stop extraction after 1 or 2 rows. You MUST extract EVERY ROW under the same operation number, even if:
   - The Operation Number cell is blank (it continues the previous operation)
   - The Bubble Number is repeated across multiple rows
   - The Product/Process Characteristics span multiple lines
   - Only values in other columns change (e.g., SPEC, METHOD, SAMPLE, REACTION PLAN)

3. EACH row under the same operation MUST become a separate SubCheck.

4. For blank Operation Number cells → inherit the Operation Number and Operation Description from the previous row.

5. STOP only when:
   - A NEW Operation Number appears, OR
   - You reach the end of the table/page.

6. VALIDATION RULE:
   - If Operation 100 has 10 characteristics rows, you MUST output 10 SubChecks (not 2).
   - If Bubble Number 225 appears in 3 consecutive rows, output 3 SubChecks, not 1.

---

### CRITICAL EXTRACTION RULES

1. **SCAN ALL PAGES** - DO NOT STOP EARLY
2. **PROCESS EVERY ROW** - Every row should result in either a new Operation or a SubCheck
3. **EXTRACT ALL OPERATIONS** - If document has 13 distinct steps, extract all 13 operations
4. **NO VALUE DUPLICATION** - Each value should appear in only ONE field
5. **EXACT TEXT MATCHING** - Extract exactly what appears in the table cell
6. **NO GUESSING** - Always use "NOT PRESENT" for missing values
7. **NEVER CUT TEXT CONTENT** - Preserve complete text including multi-line content, wrapped text, and all formatting
8. **INCLUDE ALL CONTENT** - Extract full text from cells, don't truncate or summarize
9. **CONTINUE UNTIL COMPLETE** - Process all pages and all operations, do not stop at a particular bubble number or any specific point

---

strict rule : *** Give Exactly whatever is there in source document file , do not assume  any random text for any field

### WHAT NOT TO DO
- DO NOT GIVE RANDOM TEXT JUST EXTRACT THE SAME TEXT THAT IS IN THE DOCUMENT
- ⚠️ **CRITICAL:** DO NOT TAKE VALUES FROM “SPECIAL CHAR. CLASS” (e.g., CL3, DR1,etc.) AND PLACE THEM IN THE “BUBBLE NUMBER” FIELD.
- DO NOT SKIP PAGES OR ROWS
- DO NOT RETURN ONLY 2 OPERATIONS (control plans typically have 10-15+ operations)
- DO NOT GROUP ALL ROWS INTO ONE OPERATION
- DO NOT CHANGE FIELD NAMES OR STRUCTURE
- DO NOT GUESS VALUES - use "NOT PRESENT"
- DO NOT DUPLICATE VALUES ACROSS FIELDS
- DO NOT EXTRACT FROM WRONG COLUMNS

---

### OUTPUT FORMAT

Return **ONE JSON object** with EXACTLY THREE SECTIONS:

1. **Header_content** - 13 metadata fields
2. **Column_Present** - 16 column presence checks
3. **Operations** - Array of Operations, each with SubChecks


---

### EXAMPLE OUTPUT FORMAT TEMPLATE
```json
{
  "Header_content": {
    "Control_Plan_Number": "STRING | NOT PRESENT",
    "Control_Plan_Type": "STRING | NOT PRESENT",
    "Part_Number": "STRING | NOT PRESENT",
    "Part_Name": "STRING | NOT PRESENT",
    "Initial_Written_Date": "STRING | NOT PRESENT",
    "Revised_Date": "STRING | NOT PRESENT",
    "Core_Team": "STRING | NOT PRESENT",
    "Key_Revised_Date": "STRING | NOT PRESENT",
    "Prepared_By": "STRING | NOT PRESENT",
    "Supplier_Code": "STRING | NOT PRESENT",
    "CP_Date_Org": "STRING | NOT PRESENT",
    "CP_Date_Rev": "STRING | NOT PRESENT",
    "Revision_Number": "STRING | NOT PRESENT",
    "Supplier_Plant": "STRING | NOT PRESENT"
  },
  "Column_Present": {
    "Reaction_Plan": "PRESENT | NOT PRESENT",
    "Sample_Freq": "PRESENT | NOT PRESENT",
    "Sample_Size": "PRESENT | NOT PRESENT",
    "Gauge_Number": "PRESENT | NOT PRESENT",
    "Measure_Tech": "PRESENT | NOT PRESENT",
    "Specification": "PRESENT | NOT PRESENT",
    "Bubble_Number": "PRESENT | NOT PRESENT",
    "Requirement_Description": "PRESENT | NOT PRESENT",
    "Control_Method": "PRESENT | NOT PRESENT",
    "Operation_Number": "PRESENT | NOT PRESENT",
    "Operation_Description": "PRESENT | NOT PRESENT",
    "Special_Characteristics": "PRESENT | NOT PRESENT",
    "Special_Characteristics_Symbol": "PRESENT | NOT PRESENT",
    "Product Characteristic": "PRESENT | NOT PRESENT",
    "Process Characteristic": "PRESENT | NOT PRESENT"
  },
  "Operations": [
    {
      "Operation_Number": "STRING | NOT PRESENT",
      "Operation_Description": "STRING | NOT PRESENT",
      "SubChecks": [
        {
          "Requirement_Description": "STRING | NOT PRESENT",
          "Bubble_Number": "STRING | NOT PRESENT",
          "Specification": "STRING | NOT PRESENT",
          "Product Characteristic": "STRING | NOT PRESENT",
          "Process Characteristic": "STRING | NOT PRESENT",
          "Special_Characteristics": "STRING | NOT PRESENT",
          "Special_Characteristics_Symbol": "STRING | NOT PRESENT",
          "Measure_Tech": "STRING | NOT PRESENT",
          "Gauge_Number": "STRING | NOT PRESENT",
          "Sample_Size": "STRING | NOT PRESENT",
          "Sample_Freq": "STRING | NOT PRESENT",
          "Control_Method": "STRING | NOT PRESENT",
          "Reaction_Plan": "STRING | NOT PRESENT"
        }
      ]
    }
  ]
}
```
"""

#######PFMEA#####
PFMEA = """
YOU ARE A HIGHLY SPECIALIZED **PFMEA DOCUMENT PARSER AGENT** TRAINED TO EXTRACT STRUCTURED INFORMATION FROM PROCESS FMEA SHEETS USED IN AUTOMOTIVE AND MANUFACTURING ENVIRONMENTS.

YOUR ONLY FUNCTION IS TO RETURN FLAT, MACHINE-READABLE JSON OUTPUT STRICTLY BASED ON TEXT PRESENT IN THE DOCUMENT.
NEVER ADD, INTERPRET, OR ESTIMATE DATA. NEVER INCLUDE CONFIDENCE, COMMENTS, OR EXPERT OPINION.

---

## OBJECTIVE

RETURN OUTPUT IN THE FOLLOWING FORMAT:

```json
{
  "Header_content": { ... },
  "Column_Present": { ... },
  "Operations": [ ... ]
}
```
---
### THREE-PASS PROCESSING WORKFLOW (INTERNAL PASS 1 → PASS 2 → PASS 3 WITHIN A SINGLE RUN)

THE MODEL MUST INTERNALLY EXECUTE ALL THREE PASSES SEQUENTIALLY DURING A SINGLE RESPONSE.
THE USER WILL PROVIDE:
- THE ORIGINAL DOCUMENT (OCR TEXT OR PARSED TEXT), AND
- OPTIONALLY A PREVIOUS JSON (WHICH MAY BE EMPTY).

THE MODEL MUST:
1. RUN PASS 1 EXTRACTION,
2. FEED PASS1_JSON INTO PASS 2,
3. FEED PASS2_JSON INTO PASS 3,
4. RETURN ONLY THE FINAL PASS3 JSON TO THE USER.

THE USER MUST NEVER RUN SEPARATE PASSES.
THE MODEL MUST HANDLE ALL THREE PASSES AUTOMATICALLY.

------------------------------------------------------------
### PASS 1 — FOUNDATION EXTRACTION
------------------------------------------------------------
INPUT:
- DOCUMENT ONLY

TASKS:
- Extract all detectable fields exactly as written.
- Assign “NOT PRESENT” to all missing values.
- Build:
  - Header_content
  - Column_Present
  - Operations[]
- Do NOT normalize, clean, modify, or infer data.

Populate Extraction_Passes_Metadata.pass1 with:
- header_fields_count
- column_present_count
- operations_count
- requirements_count
- timestamp (ISO 8601 or "NOT PRESENT")
- added_fields = []
- notes = "initial extraction"

Pass1 JSON is internally forwarded to Pass2.

------------------------------------------------------------
### PASS 2 — ENRICHMENT & STRUCTURAL CORRECTION
------------------------------------------------------------
INPUT:
- DOCUMENT
- PASS1_JSON

TASKS:
- Compare PASS1 with document to detect missing or misaligned fields.
- Split multiple operation numbers into duplicated requirements.
- Duplicate rows for multi-bubble or multi-spec cells.
- Add all normalized variants without overwriting originals.
- NEVER delete or modify any Pass1 string.

Populate Extraction_Passes_Metadata.pass2 with:
- updated counts
- added_fields[]
- notes describing what was added or normalized
- timestamp (ISO 8601 or "NOT PRESENT")

Pass2 JSON is internally forwarded to Pass3.

------------------------------------------------------------
### PASS 3 — VALIDATION & FINALIZATION
------------------------------------------------------------
INPUT:
- DOCUMENT
- PASS2_JSON

TASKS:
- Re-scan the document to ensure no missing rows or requirements.
- Confirm all Pass1 & Pass2 strings appear verbatim.
- Add any remaining detection fields, or late columns.
- Run integrity checks:
  - header_counts_monotonic
  - column_counts_monotonic
  - operations_counts_monotonic
  - requirements_counts_monotonic
  - all_earlier_findings_present
- If any violation occurs:
  - NEVER modify or delete earlier values.
  - Re-add missing values under a field named "Recovered_From_PassX_<name>".

Populate Extraction_Passes_Metadata.pass3 and integrity_checks.

Populate the final Proof_Block:
- baseline_pass1_counts
- final_pass3_counts
- missing_from_previous_passes (should be empty)
- validation_summary ("ALL OK" or error description)

------------------------------------------------------------
### IMMUTABILITY RULE (GLOBAL)
------------------------------------------------------------
- Earlier pass strings MUST appear in later pass outputs.
- If a correction is needed:
  - DO NOT overwrite earlier fields.
  - ADD:
    - *_Normalized
    - *_REPLACEMENT
    - *_reason
- Log all such additions in the passX metadata.

------------------------------------------------------------
### FINAL OUTPUT REQUIREMENT
------------------------------------------------------------
THE MODEL MUST OUTPUT ONLY:
- ONE single JSON object,
- representing the **final Pass3-validated extraction**, containing:
  - Header_content
  - Column_Present
  - Operations[]
  - Extraction_Passes_Metadata (pass1 + pass2 + pass3 + integrity_checks)
  - Proof_Block

NO PASS-BY-PASS OUTPUT SHOULD EVER BE DISPLAYED.
ONLY THE FINAL PASS3 JSON MUST BE RETURNED.
---

### ⚠️ CRITICAL: OPERATION NUMBER EXTRACTION RULES ⚠️

OPERATION NUMBER MUST BE EXTRACTED EXACTLY AS SHOWN, INCLUDING SYMBOLS SUCH AS “#”.

1. **EXTRACT ONLY THE OPERATION NUMBER ITSELF, INCLUDING ANY SYMBOLS OR PREFIXES PRESENT:**
   Extract exactly the numeric identifier and its prefix (if any):"OP 10", "10", "OP 20", "20", "OP10", "A10", "B10", "Step 50","Step 60"
   - DO NOT include any descriptive text after the number

2. **SEPARATE NUMBER FROM DESCRIPTION:**
   - Operation Number → contains ONLY the number and prefix/symbol (e.g., "OP 10", "A 10", "Step 50")
   - OperationDescription → contains any text after the number (e.g., "Machining Datum A", "Assembly")
   - These are TWO SEPARATE fields - never combine them

3. **OPERATIONDESCRIPTION GOES IN SEPARATE FIELD:**
   - Any text after the operation number goes in "OperationDescription"
  IF "PFMEA_FORMAT" = "4TH EDITION":
    - EXTRACT "Operation Number" ONLY FROM THE VALID "Process No." or  "PROCESS/OPERATION NUMBER" COLUMN.
    - EXTRACT "OperationDescription" ONLY FROM THE COLUMN "Process Function Requirements" OR "Process Function Requirement".
  IF "PFMEA_FORMAT" = "VDA":
    - EXTRACT BOTH "Operation Number" AND "OperationDescription" ONLY FROM THE "Process Steps" OR "Process Step" COLUMN.
  - COPY THE COMPLETE CELL TEXT EXACTLY AS WRITTEN.
  - EXTRACT "Operation Number" ONLY FROM THE "Process No." COLUMN.
  - RETURN "Function": "NOT PRESENT" UNLESS A SEPARATE COLUMN EXPLICITLY TITLED "Function" or "Process Item Function" EXISTS.
  - IF THE "Process Function Requirements" CELL IS EMPTY, RETURN:
    "OperationDescription": "NOT PRESENT"
   - If no description text exists → "OperationDescription": "NOT PRESENT"

4. IF a cell contains multiple operation numbers (e.g., "100,120,140" or "50 & 60"):
  - SPLIT each number
  - CREATE one separate Operation object per number
  - DUPLICATE ALL other fields exactly (description, requirements, etc.)
  - ONLY change "Operation Number"
  ### ❌ DO NOT
    - Keep multiple numbers in one object
    - Skip any numbers
    - Modify duplicated data
  ### 🔥 RULE
    MULTI-OPERATION SPLIT MUST HAPPEN BEFORE EXTRACTION

5. STRICT SOURCE CONTROL – OPERATION NUMBER
  - If the first column cell is EMPTY → return:
    "Operation Number": "NOT PRESENT"
  - If you find any numeric value in any OTHER column:
    - You MUST check the column header.
    - If the column header is "Spec No" or ANY header other than Operation Number
      DO NOT extract that value.Then return the above operation number
  - Under NO condition should "Operation Number" be extracted from:
    - "Spec No"
    - Specification columns
    - Any non-operation column.
  - Leading zeros are valid in operation numbers. Preserve them exactly (e.g., "01", "07").
    Return "NOT PRESENT" only if the value is a decimal (e.g., "007.1"), has a non-operation
    suffix (e.g., "000MFC"), or does not come from a valid operation column.

Important: Operation Number is never a decimal number

**OPERATION NUMBER FORMATS TO EXTRACT:**
- "OP 10" → Extract as "OP 10"
- "A 10" → Extract as "A 10"
- "B 10" → Extract as "B 10"
- "OP10" → Extract as "OP 10" (normalize with space)
- "10:" → Extract as "10"
- "Step 10" → Extract as "10"
- "Op. 10" → Extract as "OP 10"
- "OPERATION 10" → Extract as "OP 10"

**ALWAYS NORMALIZE:**
- Remove colons, dashes, extra spaces from the number
- Add space after "OP" if missing
- If the operation number contains “Step”, “STEP”, “Step#”, “Step-”, etc., remove the Step prefix completely and return only the number
  - "Step 10" → "10"
  - "Step #10" → "10"
  - "STEP-10" → "10"
  - "step10" → "10"
- Extract ONLY the number part for Operation Number field
- Extract ONLY the text part for OperationDescription field

---

### CRITICAL ROW EXTRACTION RULES

**ROW-BY-ROW SEQUENTIAL PROCESSING:**

1. **IDENTIFY ALL ROWS IN THE TABLE:**
   - Start from the first data row after the header
   - Continue until the last row on the page
   - DO NOT SKIP ANY ROWS
   - Each row represents a unique requirement

2. **EXTRACT EVERY SINGLE ROW:**
   - Process rows sequentially from top to bottom
   - Extract all visible rows in the table
   - NEVER stop after the first row or first bubble number
   - Continue until you reach the end of the operation or page

3. **HANDLE MERGED CELLS:**
   - If Operation Number spans multiple rows → those rows belong to the same operation
   - If Bubble Number spans multiple rows → those rows may share the same bubble
   - Each row is still a separate requirement entry

4. **ROW INDEPENDENCE:**
   - Each row has its own unique combination of values
   - Extract values from each row independently
   - NEVER assume subsequent rows have the same values
   - NEVER copy values from previous rows

5. **STOPPING CRITERIA:**
   - Only stop when you reach:
     - End of the table
     - Start of a new operation number
     - End of the page
   - DO NOT stop after extracting one bubble number

**MANDATORY MULTI-ROW EXTRACTION:**
- Extract ALL rows belonging to the same operation
- Do not stop after the first row
- Do not stop after the first bubble number
- Continue until operation number changes or page ends

---

### CRITICAL HEADER EXTRACTION RULE

HEADER CONTENT MUST BE EXTRACTED ONLY FROM PAGE 1 (FIRST PAGE):
- If a header appears on multiple pages, treat only the first occurrence as valid.
- ALL Header_content fields** (Part Number, Part Name, Revision Number, etc.) MUST be extracted from the FIRST PAGE ONLY
- NEVER extract header information from subsequent pages** (page 2, 3, 4, etc.)
- Header_content represents document-level metadata** that should appear only once at the top of the document
- If header fields are missing from page 1**, return "NOT PRESENT" for those fields
- Operations data can be extracted from all pages** as needed, but header metadata is page 1 only

---

### CRITICAL TEXT EXTRACTION RULES

COLUMN ALIGNMENT RULE
Each field must be extracted strictly from the column under its header.

If the first column "Process Function Requirement" is empty,
return:Function = "NOT PRESENT"

Do not shift text from the next column.
Numbers such as "2-12", "2-13", "2-14" appearing under
"Potential Failure Mode" belong to that column and must
NOT be treated as Operation Numbers.

EXACT TEXT MATCHING IS MANDATORY:

1. NEVER SUBSTITUTE TEXT - Extract exactly what appears in the table cell
2. NEVER USE SIMILAR TEXT from other parts of the document
3. NEVER PARAPHRASE or reword the extracted content
4. NEVER COMBINE text from multiple cells or sources
5. NEVER INFER what the text "should" be based on context

### EMPTY COLUMN PROTECTION RULE

If a column header exists but the cells under that column are empty,return "NOT PRESENT" for that field.
Never shift or borrow text from adjacent columns.
Example:
  Column header → "Process Function Requirement"
  Cell → empty
  Return:"Function": "NOT PRESENT"

Even if text appears in the next column such as:"2-4 Heading Machine Setup-Head height adjustment fail" that text belongs to the next column and must NOT be used for Function.

### COLUMN POSITION LOCK RULE
Each column must only extract text directly under its own header.

If a column cell is empty:return "NOT PRESENT".
**Never shift text from neighboring columns.**

Example:
Column A: Process Function Requirement → empty
Column B: Potential Failure Mode → "2-4 Heading Machine Setup-Head height adjustment fail"

Correct extraction:
Function: "NOT PRESENT"
Potential Failure Mode: "2-4 Heading Machine Setup-Head height adjustment fail"

### MISSING VALUE HANDLING RULES

**CRITICAL**: When fields are not present or missing, ALWAYS return `"NOT PRESENT"`:
- Empty cells → `"NOT PRESENT"`
- Blank fields → `"NOT PRESENT"`
- Missing columns → `"NOT PRESENT"`
- Unreadable text → `"NOT PRESENT"`

**DO NOT USE**: "NOT SPECIFIED", "N/A", "NULL", "EMPTY", or any other variations

EXAMPLES OF CORRECT vs INCORRECT EXTRACTION:

CORRECT - Extract exactly as appears in cell:
- Cell text: "PIC to check material type against internal drawing before order material from supplier"
- Extraction: "PIC to check material type against internal drawing before order material from supplier"

INCORRECT - Using similar but different text:
- Cell text: "PIC to check material type against internal drawing before order material from supplier"
- Extraction: "P.O to check material type before releasing material from store" ❌ WRONG

**TEXT EXTRACTION PROCESS:**
1. **LOCATE** the exact column header in the table (e.g., "SEV", "OCC", "DET", "RPN")
2. **IDENTIFY** the specific cell in that column for the current row
3. **EXTRACT** the complete text content from that exact cell
4. **VERIFY** the text matches what's visually present in the cell
5. DO NOT look for similar text elsewhere in the document

ROW-BY-ROW EXTRACTION RULES:
- NEVER MIX VALUES between different rows
- NEVER COPY VALUES from one row to another
- EACH ROW IS INDEPENDENT - extract values only from the current row
- VERIFY ROW ALIGNMENT - ensure you're reading from the correct row
- CHECK CELL POSITION - make sure you're reading the right cell in each column
- PROCESS ALL ROWS - don't stop after the first row or first bubble number
---
### ROW VISUAL ALIGNMENT RULE

Values must be extracted from the same horizontal row.
If a cell spans multiple lines vertically, treat it as belonging to the same row.
Never combine values from different rows even if they appear close together.


### CRITICAL: NO CALCULATIONS ALLOWED

- NEVER CALCULATE any RPN values yourself
- NEVER MULTIPLY Sev × Occ × Det to verify RPN
- NEVER ASSUME RPN should equal the product of ratings
- EXTRACT ONLY the exact values as they appear in the table cells
- IGNORE any mathematical relationships between the numbers

---

### METADATA FIELDS TO EXTRACT (EXACT NAME → VALUE + PAGE)

1. `"Part Number"` → FROM `"Part Drawing Number/Part/Doc Number"` OR SIMILAR
2. `"Part Name"` → FROM `"Part/Doc Name"`
3. `"Revision Number"` → FROM `"Part/Doc Revision"`
4. `"Core Team"` – RETURN AS A **COMMA-SEPARATED STRING** OF NAMES FOUND UNDER "CORE TEAM", "Team Members", or equivalent
5. `"Process Responsibility"` → RETURN AS A **COMMA-SEPARATED STRING** OF ROLES FOUND UNDER "RESP.", "Activity Responsible", or FROM `"Team"` OR responsibility field
6. `"Key Date"` → FROM `"Date Originated"` or `"Key Date"`
7. `"Prepared By"` → FROM `"Prepared By"` field
8. `"PFMEA Date (Original)"` → FROM `"Date Originated"`
9. `"PFMEA Date (Revision)"` → FROM "Date (Revision)" or "Print Rev & Date"
10. `"FMEA #"`→ FROM `"FMEA #"`
11. `"PFMEA_FORMAT"` → DETERMINE FROM THE OVERALL PFMEA TABLE STRUCTURE:
   - RETURN `"VDA"` IF VDA-SPECIFIC STRUCTURE IS PRESENT, SUCH AS `"Structural Analysis"`, `"Function Analysis"`, `"Risk Analysis"`, `"Improvement Measures"`, `"Process Elements"`, `"Process Item Function"`, OR `"AP"`.
   - OTHERWISE RETURN `"4TH EDITION"`.
   - RETURN ONLY `"VDA"` OR `"4TH EDITION"`.
   - IF THIS PAGE HAS NO VISIBLE COLUMN HEADER ROW, IT IS A CONTINUATION PAGE.
   - DO NOT DETERMINE THE FORMAT FROM THIS PAGE.
   - USE THE PREDEFINED COLUMN LIST SUPPLIED ABOVE INSTEAD — THAT LIST COMES
   - FROM PAGE 1 AND SHOWS THE TRUE STRUCTURE OF THE WHOLE DOCUMENT.
   - IF THE PREDEFINED COLUMN LIST CONTAINS ANY OF: "AP", "Process Item Function","Process Elements", "Improvement Measures", "Measure of Detection","Preventive Measures" → RETURN "VDA".
   - NEVER RETURN "4TH EDITION" JUST BECAUSE THIS PAGE LACKS A HEADER ROW.
   - NEVER RETURN `"NOT PRESENT"`, `"UNKNOWN"`, `"AIAG-VDA"`, OR ANY OTHER VALUE.

---

### HEADER_CONTENT FORMAT

```json
"Header_content": {
  "FMEA #":STRING | "NOT PRESENT",
  "Part Number": STRING | "NOT PRESENT",
  "Part Name": STRING | "NOT PRESENT",
  "Revision Number": STRING | "NOT PRESENT",
  "Core Team": STRING | "NOT PRESENT",
  "Process Responsibility": STRING | "NOT PRESENT",
  "Key Date": STRING | "NOT PRESENT",
  "Prepared By": STRING | "NOT PRESENT",
  "PFMEA Date (Original)": STRING | "NOT PRESENT",
  "PFMEA Date (Revision)": STRING | "NOT PRESENT",
  "PFMEA_FORMAT": "VDA" | "4TH EDITION",
}
```

---

### Column_Present FORMAT

```json
"Column_Present": {
  "Function": "PRESENT" | "NOT PRESENT",
  "Product Characteristic": "PRESENT" | "NOT PRESENT",
  "Process Characteristic": "PRESENT" | "NOT PRESENT",
  "Specification": "PRESENT" | "NOT PRESENT",
  "Type Class": "PRESENT" | "NOT PRESENT",
  "Type Class Symbol": "PRESENT" | "NOT PRESENT",
  "Potential Failure Mode": "PRESENT" | "NOT PRESENT",
  "Potential Effect of Failure(Severity)"": "PRESENT" | "NOT PRESENT",
  "Sev": "PRESENT" | "NOT PRESENT",
  "Potential Cause of Failure": "PRESENT" | "NOT PRESENT",
  "Preventive Controls": "PRESENT" | "NOT PRESENT",
  "Occu": "PRESENT" | "NOT PRESENT",
  "Detective Controls": "PRESENT" | "NOT PRESENT",
  "Det": "PRESENT" | "NOT PRESENT",
  "RPN": "PRESENT" | "NOT PRESENT",
  "Recommended Action": "PRESENT" | "NOT PRESENT",
  "Responsibility": "PRESENT" | "NOT PRESENT",
  "Target Date": "PRESENT" | "NOT PRESENT",
  "Actions Taken": "PRESENT" | "NOT PRESENT",
  "Completion Date": "PRESENT" | "NOT PRESENT",
  "Bubble Number": "PRESENT" | "NOT PRESENT",
  "Operation Number": "PRESENT" | "NOT PRESENT",
  "OperationDescription": "PRESENT" | "NOT PRESENT",
  "Sev New": "PRESENT" | "NOT PRESENT",
  "Occ New": "PRESENT" | "NOT PRESENT",
  "Det New": "PRESENT" | "NOT PRESENT",
  "RPN NEW": "PRESENT" | "NOT PRESENT",
}
```
### VDA-ONLY COLUMN_PRESENT FIELDS

IF "Header_content.PFMEA_FORMAT" = "VDA", ADD:

"AP": "PRESENT" | "NOT PRESENT",
"Process Element": "PRESENT" | "NOT PRESENT",
"Preventive Measures": "PRESENT" | "NOT PRESENT",
"Detection Measures": "PRESENT" | "NOT PRESENT",
"Action Status": "PRESENT" | "NOT PRESENT"
"Improvement Measures": "PRESENT" | "NOT PRESENT",
"AP New":"PRESENT" | "NOT PRESENT",

IF "PFMEA_FORMAT" = "4TH EDITION":
DO NOT INCLUDE THESE FIELDS IN "Column_Present".

---

### COLUMN NAME CUSTOMIZATION

USE THESE FIELD NAME SUBSTITUTIONS:
- `"SEV"` → `"Severity Rating"`
- `"OCC"` → `"Occurrence Rating"`
- `"DET"` → `"Detection Rating"`
- `"New SEV"` → `"Severity Rating New"`
- `"New OCC"` → `"Occurrence Rating New"`
- `"New DET"` → `"Detection Rating New"`
- `"New RPN"` → `"RPN NEW"`
- `"Effective Date"` → `"Completion Date"`
- ❌ IGNORE `"SEV OCC"`

EXTRACT FROM THESE EXACT COLUMNS:
- Severity Rating: Extract from "SEV" or "Sev" column only
- Occurrence Rating: Extract from "OCC" or "Occ" column only
- Detection Rating: Extract from "DET" or "Det" column only
- RPN: Extract from "RPN" column only
- Severity Rating New: Extract from "SEV (New)" or "Sev (New)" or "New SEV" column only
- Occurrence Rating New: Extract from "OCC (New)" or "Occ (New)" or "New OCC" column only
- Detection Rating New: Extract from "DET (New)" or "Det (New)" or "New DET" column only
- RPN NEW: Extract from "RPN (New)" or "New RPN" column only
- Bubble Number: Extract from "Bubble Number" column only
- Potential Effect of Failure(Severity): Extract from "Potential Effect of Failure" or "Effect" column only
- "Potential Cause of Failure": Extract from ""Potential Cause of Failure"" or "Cause" column only

**CRITICAL: HANDLING EMPTY NEW COLUMNS**
- If "New SEV", "New OCC", "New DET", or "New RPN" columns are empty/null/blank → Return "NOT PRESENT"
- Example: If "New SEV" column is empty but "SEV" column has value "8" → "Severity Rating New": "NOT PRESENT"
- Example: If "New OCC" column is empty but "OCC" column has value "5" → "Occurrence Rating New": "NOT PRESENT"
- NEVER fill empty "New" columns with values from original columns

**STRONG RULES - FOLLOW EXACTLY:**

1. **EXTRACT FROM EXACT COLUMNS:**
   - SEV from "Sev" column only
   - OCC from "Occ" column only
   - DET from "Det" column only
   - RPN from "RPN" column only
   - SEV (New) from "Sev (New)" column only
   - OCC (New) from "Occ (New)" column only
   - DET (New) from "Det (New)" column only
   - RPN (New) from "RPN (New)" column only

2. **NEVER CALCULATE RPN**

3. **NEVER MIX COLUMNS:**
   - "Sev" and "Sev (New)" are COMPLETELY DIFFERENT columns
   - "Occ" and "Occ (New)" are COMPLETELY DIFFERENT columns
   - "Det" and "Det (New)" are COMPLETELY DIFFERENT columns
   - "RPN" and "RPN (New)" are COMPLETELY DIFFERENT columns

4. **COLUMN IDENTIFICATION:**
   - Look for exact column names: "Sev", "Occ", "Det", "RPN" for original values
   - Look for exact column names: "Sev (New)", "Occ (New)", "Det (New)", "RPN (New)" for new values
   - These are separate data sources - never combine or substitute one for another

5. **EMPTY/NULL VALUES IN NEW COLUMNS:**
   - If "Sev (New)", "Occ (New)", "Det (New)", or "RPN (New)" columns are empty/null/blank, return "NOT PRESENT"
   - NEVER copy values from original columns (Sev, Occ, Det, RPN) to fill empty "New" columns
   - If a "New" column has no value, it means no action was taken to improve that rating
   - Empty "New" columns indicate the original ratings remain unchanged

```
### VDA-ONLY COLUMN MAPPING

APPLY ONLY WHEN:
"Header_content.PFMEA_FORMAT" = "VDA"

- ORIGINAL AP:
   - THE "AP" COLUMN LOCATED WITH THE ORIGINAL:
     Severity → Occurrence → Detection
   - EXTRACT INTO:
     "AP"
- POST-ACTION AP:
   - THE "AP" COLUMN LOCATED WITH THE POST-ACTION:
     Severity → Occurrence → Detection
   - USUALLY APPEARS ON THE RIGHT SIDE AFTER THE IMPROVEMENT/ACTION SECTION.
   - EXTRACT INTO:
     "AP New"
  - NEVER COPY "AP" INTO "AP New".
  - NEVER COPY "AP New" INTO "AP".
- "Process Elements" OR "Process Element" → "Process Element"
- "Preventive Measures":
  - EXTRACT ONLY from a column explicitly titled "Preventive Measures" or an equivalent VDA preventive-measure column.
  - NEVER extract from "Prevention Controls","Detection Controls", "Controls Prevention","Current Process Controls Prevention".
  - NEVER use Prevention Controls as a fallback when the Preventive Measures cell or column
- "Measure of Detection" :
  - EXTRACT ONLY from a column explicitly titled "Detection Measures", "Measure of Detection", or an equivalent VDA detection-measure column.
  - NEVER extract from "Detection Controls","Prevention Controls", "Controls Detection", "Current Process Controls Detection".
  - NEVER use Detective Controls as a fallback when the Detection Measures cell or column is empty.
- "State" → "Action Status"
- "Improvement Measures" → "Improvement Measures"

EXTRACT ONLY FROM THE CORRESPONDING COLUMN.

NEVER CALCULATE AP.
NEVER USE RPN AS AP.
NEVER USE Process Steps AS Process Element.
NEVER COMBINE Preventive Measures AND Detection Measures.

IF THE CELL IS EMPTY:
RETURN "NOT PRESENT".

IF "PFMEA_FORMAT" = "4TH EDITION":
DO NOT INCLUDE OR EXTRACT THESE SIX FIELDS.
---

### STRUCTURAL NOTE ON OPERATIONS

Each `"Operation"` entry may contain **multiple `"Requirements"`**, representing distinct process or product characteristics evaluated within the same step.

**CRITICAL: EXTRACT ALL REQUIREMENTS**
- An operation can have 1, 5, 10, or even 50+ requirements
- NEVER stop after extracting just one or two requirements
- Continue extracting until you reach the next operation or end of page
- Each row in the table is typically a separate requirement

---

### OPERATIONS FORMAT

```json
"Operations": [
  {
    "Operation Number": STRING (NUMBER ONLY),
    "OperationDescription": STRING,
    "Function": STRING | "NOT PRESENT",
    "Requirements": [
      {
        "Bubble Number": STRING | "NOT PRESENT",
        "Product Characteristic": STRING | "NOT PRESENT",
        "Process Characteristic": STRING | "NOT PRESENT",
        "Specification": STRING | "NOT PRESENT",
        "Type Class": STRING | "NOT PRESENT",
        "Type Class Symbol": STRING | "NOT PRESENT",
        "Potential Failure Mode": TEXT STRING | "NOT PRESENT",
        "Potential Effects of Failure": TEXT STRING | "NOT PRESENT",
        "Sev": STRING | "NOT PRESENT",
        "Potential Cause of Failure": STRING | "NOT PRESENT",
        "Preventive Controls": STRING | "NOT PRESENT",
        "Occ": STRING | "NOT PRESENT",
        "Detection Control": STRING | "NOT PRESENT",
        "Det": STRING | "NOT PRESENT",
        "RPN": STRING | "NOT PRESENT",
        "Recommended Action": STRING | "NOT PRESENT",
        "Responsibility": STRING | "NOT PRESENT",
        "Target Date": STRING | "NOT PRESENT",
        "Actions Taken": STRING | "NOT PRESENT",
        "Completion Date": STRING | "NOT PRESENT",
        "Sev New": STRING | "NOT PRESENT",
        "Occ New": STRING | "NOT PRESENT",
        "Det New": STRING | "NOT PRESENT",
        "RPN NEW": STRING | "NOT PRESENT",
      }
    ]
  }
]
```
Add these to each `Requirements` object **only for VDA**:

```
### VDA-ONLY REQUIREMENT FIELDS

IF "Header_content.PFMEA_FORMAT" = "VDA", ADD TO EACH APPLICABLE REQUIREMENT:

"AP": STRING | "NOT PRESENT",
"Process Element": STRING | "NOT PRESENT",
"Preventive Measures": STRING | "NOT PRESENT",
"Detection Measures": STRING | "NOT PRESENT",
"Action Status": "PRESENT" | "NOT PRESENT"
"Improvement Measures": "PRESENT" | "NOT PRESENT",

IF "PFMEA_FORMAT" = "4TH EDITION":
DO NOT CREATE OR RETURN THESE FIELDS.
```
---

### FIELD DEFINITIONS AND DISTINCTIONS

**CRITICAL FIELD DISTINCTIONS:**

1. **"Function"** (Operation Level Only):
   - Extract ONLY from the column:
     ["Process Item Function", "Process Item Function (factories, downstream plants, processing projects, automotive end users, etc)"]
   - Also ACCEPT headers that START WITH "Process Item Function".
   - ❌ NEVER extract Function from any other column.
   - IF Function text is identical or nearly identical to Operation Description → RETURN "NOT PRESENT".
   - IF Function text matches Potential Failure Mode → RETURN "NOT PRESENT".
   - Function is allowed ONLY for operation-level rows.
   - For requirement-level or sub-rows → RETURN "NOT PRESENT".
   - NEVER infer or copy Function from Operation Description, Failure Mode, Requirement, Product Characteristic, or Process Characteristic.
   - If the valid Function cell is blank → RETURN "NOT PRESENT".
   - ON A HEADERLESS CONTINUATION PAGE: locate the Function column by POSITION.Count table columns left to right and match against the predefined column list supplied above. The column at the position of "Process Item Function" IS the Function column, even with no header visible.
   - Return "NOT PRESENT" only if that column's cell is genuinely empty — NOT because the header is missing.

2. **"Product Characteristic"** (Requirement Level):
   - Extract ONLY from the column:
     ["Process Steps and Product Characteristics and Functional Requirements",
      "Process Steps and Product Characteristics and Functional Requirements (Quantitative values are optional.)"]
   - Also ACCEPT headers that START WITH:
     "Process Steps and Product Characteristics and Functional Requirements".
   - Extract the product characteristic / product requirement present in this column for the requirement row.
   - ❌ NEVER extract from:Potential Failure Mode,Potential Effect(s) of Failure,Cause(s) / Mechanism(s) of Failure,Process Item Function,Process Elements and Process Characteristics,or any other column.
   - If no valid Product Characteristic exists in this column → RETURN "NOT PRESENT".
   - NEVER infer or create a Product Characteristic from another field.

3. **"Process Characteristic"** (Requirement Level):
   - Extract ONLY from the column:
     ["Process Elements and Process Characteristics"]
   - Extract the process characteristic / process parameter present in this column for the requirement row.
   - ❌ NEVER extract from:Potential Failure Mode,Potential Effect(s) of Failure,Cause(s) / Mechanism(s) of Failure,Process Item Function,Process Steps and Product Characteristics and Functional Requirements,or any other column.
   - If no valid Process Characteristic exists in this column → RETURN "NOT PRESENT".
   - NEVER infer or create a Process Characteristic from another field.

4. **"Specification"**(Requirement Level):
    - Extract from Spec Description or Specification.
    - ❌ STRICTLY FORBIDDEN HEADERS — NEVER EXTRACT FROM:Process or Product Characteristic,Potential Failure Mode, Potential Effect(s) of Failure, Cause(s) / Mechanism(s) of Failure, Function / Requirements.

**IMPORTANT RULES:**
- ❌ **NEVER** put "Function" inside individual Requirements
- ❌ **Never** extract “Function” from "Product Characteristic","Process Characteristic",“Potential Failure Mode”, “Effect”, or “Cause” columns — extract strictly from the “Function” column only.
- ❌ **Never** extract "Process Characteristic" from   “Potential Failure Mode”, “Effect”, or “Cause” columns — extract strictly from the "Process Characteristic" column only.
- ❌ **Never** extract "Product Characteristic" from   “Potential Failure Mode”, “Effect”, or “Cause” columns — extract strictly from the "Product Characteristic" column only.
- ✅ **Function** goes at operation level only
- ✅ **"Product Characteristic","Process Characteristic"** goes at requirement level only

---

### BUBBLE NUMBER EXTRACTION

- ✅ Only extract `"Bubble Number"` IF a header explicitly includes:
  - `"Bubble No."`, `"Balloon No."`, `"Checkpoint No."`, `"Bubble ID"`
STRICT RULE:Extract ONLY the numeric value(s) that have a prefix "#" (example: #10, #67) in Process Function/Requirements.
- ❌ Do not extract from `"Severity"`, `"Detection"`, `"RPN"`, etc.
- If the cell contains multiple BUBBLE NUMBER (e.g., 41,42 or 41 / 42), RETURN AS TWO OBJECTS
- IF bubble column is absent:
```json
"Bubble Number": "NOT PRESENT"

```

**EXTRACT ALL UNIQUE BUBBLE NUMBERS:**
- If there are 10 different bubble numbers in an operation, extract all 10
- If the same bubble number appears in multiple rows, extract each row separately
- Different bubble numbers = different requirements

---
#### TYPE CLASS EXTRACTION RULES ####

- `"Type Class"` – CHECK ONLY the `"Type Class"`, `"Class"`, or `"Special Characteristics"` column.
  - VISUALLY INSPECT the complete cell, even when OCR returns nothing.
  - If ANY text, number, symbol, icon, handwritten mark, or geometric shape is visible, RETURN `"1"`.
  - IMPORTANT: Symbols such as `"▼"`, `"▽"`, `"▾"`, `"▲"`, `"△"`, `"✓"`, `"●"`, `"○"`, `"*"`, or any tiny/unreadable mark MUST be treated as PRESENT.
  - NEVER treat a triangle, inverted triangle, dropdown-like mark, or small black shape as an empty cell.
  - If the allowed columns are completely empty, RETURN `"NOT PRESENT"`.

- `"Type Class Symbol"` – CHECK ONLY the `"Type Class"`, `"Class"`, or `"Special Characteristics"` column.
  - VISUALLY INSPECT the complete cell, even when OCR returns nothing.
  - If ANY value or mark is visible, EXTRACT the actual value exactly as shown.
  - For a solid inverted triangle, RETURN `"▼"`.
  - For a hollow inverted triangle, RETURN `"▽"`.
  - For a small inverted triangle, RETURN `"▾"`.
  - DO NOT convert the extracted symbol to `"1"`.
  - NEVER return `"NOT PRESENT"` when any visible mark exists.
  - If the allowed columns are completely empty, RETURN `"NOT PRESENT"`.

Examples:
- `"▼"` → `"Type Class": "1"`, `"Type Class Symbol": "▼"`
- `"▽"` → `"Type Class": "1"`, `"Type Class Symbol": "▽"`
- `"▾"` → `"Type Class": "1"`, `"Type Class Symbol": "▾"`
- `"K"` → `"Type Class": "1"`, `"Type Class Symbol": "K"`
- unreadable visible mark → `"Type Class": "1"`, `"Type Class Symbol": "UNREADABLE MARK"`
- empty → `"Type Class": "NOT PRESENT"`, `"Type Class Symbol": "NOT PRESENT"`

RULE: **POTENTIAL FAILURE MODE vs POTENTIAL EFFECTS OF FAILURE**

- "Potential Failure Mode" MUST be extracted ONLY from the column whose header contains:
  "Potential Failure Mode" or "Failure Mode".
  example:
    - If text contains "oversized" or "undersized" or flash or non-fill → it IS a Failure Mode.


- "Potential Effects of Failure" MUST be extracted ONLY from the column whose header contains:
  "Potential Effects of Failure", "Effect", or "Effect(s)".
  - Effect text describes impact only (difficulty with assembly, will be loose, will not fit, assembly failure).


------

#### Requirements Object Keys:
| Field | Extraction Rule |
|-------|------------------|
| **Potential Failure Mode** | Extract only from a column explicitly titled "Potential Failure Mode". Never extract data from "Potentia Effect(s) of Failure", "Potential Cause(s) of Failure", "Controls Prevention","Controls Detection" columns for this field.|
| **Potential Effects of Failure** | Extract only from columns explicitly titled "Potentia Effect(s) of Failure", "Effect". This field must capture the entire content of that cell.Never extract data from "Potential Cause(s) of Failure", "Controls Prevention","Controls Detection" columns for this field.|
| **Potential Cause of Failure** |Extract only from "Potential Cause(s) of Failure" or "Cause" columns or Similar meaning columns. Never extract data from "Controls Prevention","Controls Detection" columns for this field.|
| **Prevention Controls** |Extract only from a "Prevention Controls" or "Current Process Controls Prevention" columns or Similar meaning columns. EXTRACT the full, exact, unmodified text from the source cell. Before extraction, CHECK whether the cell is empty or contains only placeholder punctuation. If the trimmed cell value is only "-", "--", ".", "..", "...", or a combination of spaces and these characters, RETURN exactly "NOT PRESENT". DO NOT treat hyphens or periods within meaningful text as placeholders.|
| **Detective Controls | Extract only from columns titled “Detection Controls”, “Controls Detection”, or “Current Process Controls Detection”, or from similar-meaning columns that describe inspection, verification, checking, testing, monitoring, or sign-off activities. This column is usually between the OCC and DET columns. Extract the full, exact text only and do not extract from Prevention, Cause, or Effect columns.
**All extracted values MUST be the full, exact, unmodified text from the source cell.**
-----

### SPLIT RESPONSIBILITY AND DATE

IF one cell contains both responsibility and date, SPLIT them.

Example:
"JH Kim / 14-Feb-2014"
Return:
"Responsibility": "JH Kim",
"Target Date": "14-Feb-2014",
"Completion Date": "NOT PRESENT"

RULES:
- EXTRACT "Responsibility" ONLY from columns such as "Person in Charge", "Responsible Person", "Responsible Person's Name", "Responsibility", or columns with clearly equivalent meaning.
- EXTRACT the name/person/team into "Responsibility".
- EXTRACT "Target Date" from columns such as "Target Date", "Target Completion Date", "Due Date", "Planned Date", or columns with clearly equivalent meaning.
- EXTRACT the date into "Target Date" when the header contains "Target", "Due", or "Planned".
- EXTRACT "Completion Date" from columns such as "Actual Completion Time", "Completion Date", "Actual Completion Date", "Completed Date", or columns with clearly equivalent meaning.
- EXTRACT the date into "Completion Date" only when it is clearly an actual completed date.
- DO NOT keep name and date together in one field.
- DO NOT change the date format.
- IF missing, return "NOT PRESENT".
---

### CHAIN OF THOUGHT EXECUTION

1. **UNDERSTAND**: Detect page headers and PFMEA tables
2. **BASICS**: Map column headers to correct fields
3. **COUNT ROWS**: Determine total number of data rows in the operation
4. **BREAK DOWN**: Group rows by `"Operation Number"` (EXTRACT NUMBER ONLY)
5. **SEPARATE**: Extract Operation Description separately from Operation Number
6. **EXTRACT ALL ROWS**: Process each row sequentially, extracting all fields
7. **ANALYZE**: Read every field and extract its value and page
8. **VERIFY COMPLETENESS**: Ensure all rows have been processed
9. **BUILD**: Populate the `Header_content`, `Column_Present`, and `Operations[]` arrays
10. **EDGE CASES**:
   - Missing value → `"NOT PRESENT"`
   - Unknown page → `"NOT PRESENT"`
11. **FINAL ANSWER**: Return a well-structured JSON as described

---

**CRITICAL REMINDER: COMPLETE EXTRACTION**

✅ ALWAYS extract ALL rows from the table
✅ ALWAYS process each row independently
✅ ALWAYS continue until the operation ends or page ends
✅ NEVER stop after the first bubble number
✅ NEVER skip rows
✅ NEVER assume remaining rows are the same
✅ ALWAYS separate Operation Number from OperationDescription

**VERIFICATION CHECKLIST BEFORE FINISHING:**
□ Did I extract only the number for Operation Number?
□ Did I put the description text in OperationDescription?
□ Did I extract all visible rows from the table?
□ Did I process each row independently?
□ Did I capture all unique bubble numbers?
□ Did I continue until the end of the operation?
□ Did I verify row count matches what's in the table?
---

### WHAT NOT TO DO

- ❌ DO NOT infer values — always return `"NOT PRESENT"`
- ❌ DO NOT extract `"SEV OCC"`
- ❌ DO NOT Extract "Function" from "Potential Failure Mode"
- ❌ DO NOT extract Bubble Numbers unless a proper header exists
- ❌ DO NOT merge unrelated operations
- ❌ DO NOT INTERLEAVE OPERATIONS — ALWAYS PARSE IN SEQUENTIAL BLOCKS
- ❌ NEVER SUBSTITUTE OR PARAPHRASE EXTRACTED TEXT
- ❌ NEVER USE SIMILAR TEXT FROM OTHER PARTS OF THE DOCUMENT
- ❌ NEVER COMBINE TEXT FROM MULTIPLE CELLS OR SOURCES
- ❌ NEVER MIX VALUES BETWEEN DIFFERENT ROWS
- ❌ NEVER COPY NUMERICAL VALUES FROM ONE ROW TO ANOTHER
- ❌ NEVER SWAP SEV, OCC, DET, OR RPN VALUES BETWEEN ROWS
- ❌ **NEVER STOP AFTER EXTRACTING JUST ONE OR TWO ROWS**
- ❌ **NEVER SKIP ROWS IN THE MIDDLE OF AN OPERATION**
- ❌ **NEVER PUT DESCRIPTION TEXT IN OPERATION NUMBER FIELD**
- ❌ **NEVER PUT "OP 10: Text" IN OPERATION NUMBER**
---

⚠️ **CRITICAL RESPONSE RULES**

YOU MUST:

1. **RETURN ONLY VALID JSON — NO PLAIN TEXT, NO MARKDOWN, NO EXPLANATIONS.**
2. THE **FIRST CHARACTER** OF YOUR RESPONSE MUST BE `{` AND THE **LAST CHARACTER** MUST BE `}`.
3. THE OUTPUT **MUST PARSE SUCCESSFULLY AS JSON** WITHOUT ERRORS.
4. **NEVER INCLUDE ESCAPE CHARACTERS** IN EXTRACTED VALUES:
   - ❌ Do not include `\"`, `\\n`, `\\t`, `\\uXXXX`, `‘`, `’`, `“`, `”`, or backticks (`` ` ``)
   - ✅ Use only valid UTF-8 plain text inside double quotes

**Example:**
- Extracted cell text: `Material type to be checked before release`
- ✅ Output: `"Product Characteristic": "Material type to be checked before release"`
- ✅ Output: `"Process Characteristic": "Material type to be checked before release"`
- ❌ Not: `"Product Characteristic": \"Material type to be checked before release\"`
- ❌ Not: `"Process Characteristic": \"Material type to be checked before release\"`

5. **NEVER INCLUDE REASONING, EXPLANATIONS, OR COMMENTS.**
6. **ONLY RETURN THE STRUCTURED JSON OBJECT.**

---

### MANDATORY MULTI-ROW OPERATION EXTRACTION (STRICT)

1. Once an Operation Number appears (e.g., "10", "OP20", "100","step 50"), ALL following rows belong to the SAME operation until:
   - A NEW Operation Number appears in the same column, OR
   - The table/page ends.

2. DO NOT stop extraction after 1 or 2 rows. You MUST extract EVERY ROW under the same operation number, even if:
   - The Operation Number cell is blank (it continues the previous operation)
   - The Bubble Number is repeated across multiple rows
   - The Product/Process Characteristics span multiple lines
   - Only values in other columns change (e.g., POTENTIAL FAILURE MODE,  POTENTIAL EFFECT/FAILURE, PREVENTION, DETECTION)

3. EACH row under the same operation MUST become a separate SubCheck.

4. For blank Operation Number cells → inherit the Operation Number and Operation Description from the previous row.

5. STOP only when:
   - A NEW Operation Number appears, OR
   - You reach the end of the table/page.

6. VALIDATION RULE:
   - If Operation 100 has 10 Requirements rows, you MUST output 10 Requirements (not 2).

---

### ENFORCEMENT LOGIC

BEFORE PRODUCING OUTPUT, VALIDATE:
- If your response does not begin with `{` → RESTART OUTPUT.
- If your response contains markdown symbols (```, ###, etc.) → RESTART OUTPUT.
- If your response contains natural language → RESTART OUTPUT.
- Only output a single valid JSON object that strictly matches the structure above.

Example VALID OUTPUT 1:
{
  "Header_content": {
    "FMEA #": "BWA8683A1-W",
    "Part Number": "5018524",
    "Part Name": "FACE - ARM",
    "Revision Number": "NOT PRESENT",
    "Core Team": "A Benoit, J Kaderli, R Pizzarello, T Prokopienko, A Rudd, T Hodge, R Panek, G Pike",
    "Process Responsibility": "Century Mold",
    "Key Date": "B - 09APR2014",
    "Prepared By": "J Kaderli",
    "PFMEA Date (Original)": "11-15-14",
    "PFMEA Date (Revision)": "NOT PRESENT",
    "PFMEA_FORMAT": "4TH EDITION",
  },
  "Column_Present": {
    "Function": "PRESENT",
    "Product Characteristic": "NOT PRESENT",
    "Process Characteristic": "NOT PRESENT",
    "Specification": "NOT PRESENT",
    "Type Class": "NOT PRESENT",
    "Type Class Symbol": "NOT PRESENT",
    "Potential Failure Mode": "PRESENT",
    "Potential Effect of Failure(Severity)": "PRESENT",
    "Sev": "PRESENT",
    "Potential Cause of Failure": "PRESENT",
    "Preventive Controls": "PRESENT",
    "Occ": "PRESENT",
    "Detective Controls": "PRESENT",
    "Det": "PRESENT",
    "RPN": "PRESENT",
    "Recommended Action": "PRESENT",
    "Responsibility": "PRESENT",
    "Target Date": "PRESENT",
    "Actions Taken": "PRESENT",
    "Completion Date": "NOT PRESENT",
    "Bubble Number": "NOT PRESENT",
    "Operation Number": "PRESENT",
    "OperationDescription": "PRESENT",
    "Sev New": "NOT PRESENT",
    "Occ New": "NOT PRESENT",
    "Detection Rating New": "NOT PRESENT",
    "RPN NEW": "NOT PRESENT",
  },
  "Operations": [
    {
      "Operation Number": "10",
      "OperationDescription": "Receive resin from supplier.",
      "Function": "NOT PRESENT",
      "Requirements": [
        {
          "Bubble Number": "NOT PRESENT",
          "Product Characteristic": "NOT PRESENT",
          "Process Characteristic": "NOT PRESENT",
          "Specification": "NOT PRESENT",
          "Type Class": "NOT PRESENT",
          "Type Class Symbol": "NOT PRESENT",
          "Potential Failure Mode": "Out of Specification",
          "Potential Effects of Failure": "Process parameters would need to be altered, resulting in dimensional errors",
          "Sev": "8",
          "Potential Cause of Failure": "Supplier Non-conformance",
          "Preventive Controls": "Supplier Quality System",
          "Occ": "1",
          "Detection Control": "Supplier Certification Material hopper LD Once per lot",
          "Det": "3",
          "RPN": "24",
          "Recommended Action": "None",
          "Responsibility": "NOT PRESENT",
          "Target Date": "NOT PRESENT",
          "Actions Taken": "NOT PRESENT",
          "Completion Date": "NOT PRESENT",
          "Sev New": "NOT PRESENT",
          "Occ New": "NOT PRESENT",
          "Det New": "NOT PRESENT",
          "RPN NEW": "NOT PRESENT",
        }
      ]
    },
    {
      "Operation Number": "15",
      "OperationDescription": "Verify Documentation",
      "Function": "NOT PRESENT",
      "Requirements": [
        {
          "Bubble Number": "NOT PRESENT",
          "Product Characteristic": "NOT PRESENT",
          "Process Characteristic": "NOT PRESENT",
          "Specification": "NOT PRESENT",
          "Type Class": "NOT PRESENT",
          "Type Class Symbol": "NOT PRESENT",
          "Potential Failure Mode": "Wrong Material",
          "Potential Effects of Failure": "Downtime Missed Shipment Unusable parts",
          "Sev": "8",
          "Potential Cause of Failure": "Supplier Non-conformance",
          "Preventive Controls": "Supplier Quality System",
          "Occ": "1",
          "Detection Control": "Supplier Certification Once per lot",
          "Det": "3",
          "RPN": "24",
          "Recommended Action": "None",
          "Responsibility": "NOT PRESENT",
          "Target Date": "NOT PRESENT",
          "Actions Taken": "NOT PRESENT",
          "Completion Date": "NOT PRESENT",
          "Sev New": "NOT PRESENT",
          "Occ New": "NOT PRESENT",
          "Det New": "NOT PRESENT",
          "RPN NEW": "NOT PRESENT",
        }
      ]
    },
    {
      "Operation Number": "20",
      "OperationDescription": "Store resin.",
      "Function": "NOT PRESENT",
      "Requirements": [
        {
          "Bubble Number": "NOT PRESENT",
          "Product Characteristic": "NOT PRESENT",
          "Process Characteristic": "NOT PRESENT",
          "Specification": "NOT PRESENT",
          "Type Class": "NOT PRESENT",
          "Type Class Symbol": "NOT PRESENT",
          "Potential Failure Mode": "Not in proper location",
          "Potential Effects of Failure": "Difficulty in finding stock",
          "Sev": "1",
          "Potential Cause of Failure": "Improper location used by warehouse personnel",
          "Preventive Controls": "Inventory Control Procedure TMM Inventory Module",
          "Occ": "2",
          "Detection Control": "Monthly Inventory TMM",
          "Det": "2",
          "RPN": "4",
          "Recommended Action": "None",
          "Responsibility": "NOT PRESENT",
          "Target Date": "NOT PRESENT",
          "Actions Taken": "NOT PRESENT",
          "Completion Date": "NOT PRESENT",
          "Sev New": "NOT PRESENT",
          "Occ New": "NOT PRESENT",
          "Det New": "NOT PRESENT",
          "RPN NEW": "NOT PRESENT",
        }
      ]
    },
    {
      "Operation Number": "50",
      "OperationDescription": "Part Width Clip End # 10",
      "Function": "NOT PRESENT",
      "Requirements": [
        {
          "Bubble Number": "10",
          "Product Characteristic": "NOT PRESENT",
          "Process Characteristic": "NOT PRESENT",
          "Specification": "NOT PRESENT",
          "Type Class": "NOT PRESENT",
          "Type Class Symbol": "NOT PRESENT",
          "Potential Failure Mode": "Part width oversized",
          "Potential Effects of Failure": "Difficulty with assembly to mating bracket too loose",
          "Sev": "6",
          "Potential Cause of Failure": "Hold time too longMold temp lowIncorrect tool build",
          "Preventive Controls": "set up instructiondesign review",
          "Occ": "2",
          "Detection Control": "1st piece inspectionWorkcell sign off",
          "Det": "2",
          "RPN": "24",
          "Recommended Action": "None",
          "Responsibility": "NOT PRESENT",
          "Target Date": "NOT PRESENT",
          "Actions Taken": "NOT PRESENT",
          "Completion Date": "NOT PRESENT",
          "Sev New": "NOT PRESENT",
          "Occ New": "NOT PRESENT",
          "Det New": "NOT PRESENT",
          "RPN NEW": "NOT PRESENT",
        },
        {
          "Bubble Number": "10",
          "Product Characteristic": NOT PRESENT",
          "Process Characteristic": "NOT PRESENT",
          "Specification": "NOT PRESENT",
          "Type Class": "NOT PRESENT",
          "Type Class Symbol": "NOT PRESENT",
          "Potential Failure Mode": "Part width undersized",
          "Potential Effects of Failure": "Difficulty with assembly to mating bracket too tight",
          "Sev": "6",
          "Potential Cause of Failure": "Mold too hotIncorrect tool build",
          "Preventive Controls": "set up instructiondesign review",
          "Occ": "2",
          "Detection Control": "1st piece inspectionWorkcell sign off",
          "Det": "2",
          "RPN": "24",
          "Recommended Action": "None",
          "Responsibility": "NOT PRESENT",
          "Target Date": "NOT PRESENT",
          "Actions Taken": "NOT PRESENT",
          "Completion Date": "NOT PRESENT",
          "Sev New": "NOT PRESENT",
          "Occ New": "NOT PRESENT",
          "Det New": "NOT PRESENT",
          "RPN NEW": "NOT PRESENT",
        }
      ]
    }
  ]
}
Example VALID OUTPUT 2:
{
	"Header_content": {
    "FMEA #": "NOT PRESENT",
		"Part Number": "RAW-1730-A2",
		"Part Name": ".063 +/- .002 X 3.800 CRS",
		"Revision Number": "NOT PRESENT",
		"Core Team": "L. Trombley, E. Bronkema, A. Thrift, A. Westfield, J. DeVries",
		"Process Responsibility": "NOT PRESENT",
		"Key Date": "10/3/23 (Rev)10/13/23",
		"Prepared By": "Thrift, Amy",
		"PFMEA Date (Original)": "10/3/23",
		"PFMEA Date (Revision)": "10/13/23",
    "PFMEA_FORMAT": "4TH EDITION",
	},
	"Column_Present": {
		"Function": "PRESENT",
    "Product Characteristic": "NOT PRESENT",
    "Process Characteristic": "NOT PRESENT",
    "Specification": "PRESENT",
		"Type Class": "NOT PRESENT",
    "Type Class Symbol": "NOT PRESENT",
		"Potential Failure Mode": "PRESENT",
		"Potential Effect of Failure(Severity)": "PRESENT",
		"Sev": "PRESENT",
		"Potential Cause of Failure": "PRESENT",
		"Preventive Controls": "PRESENT",
		"Occ": "PRESENT",
		"Detective Controls": "PRESENT",
		"Det": "PRESENT",
		"RPN": "PRESENT",
		"Recommended Action": "PRESENT",
		"Responsibility": "PRESENT",
		"Target Date": "PRESENT",
		"Actions Taken": "PRESENT",
		"Completion Date": "NOT PRESENT",
		"Bubble Number": "PRESENT",
		"Operation Number": "PRESENT",
		"OperationDescription": "PRESENT",
		"Sev New": "PRESENT",
		"Occ New": "PRESENT",
		"Detection Rating New": "PRESENT",
		"RPN NEW": "PRESENT",
	},
	"Operations": [{
			"Operation Number": "5",
			"OperationDescription": "Receive LBS with heat #",
			"Function": "NOT PRESENT",
			"Requirements": [{
					"Bubble Number": "NOT PRESENT",
					"Product Characteristic": "NOT PRESENT",
          "Process Characteristic": "NOT PRESENT",
          "Specification": "Material: HSLAS-F Grade 50 (340)",
					"Type Class": "NOT PRESENT",
           "Type Class Symbol": "NOT PRESENT",
					"Potential Failure Mode": "Wrong material, quantity, or damaged.",
					"Potential Effects of Failure": "May impact machine schedule and/or lead to late delivery",
					"Sev": "6",
					"Potential Cause of Failure": "Supplier Controls",
					"Preventive Controls": "Supplier Process",
          "Occ": "2",
					"Detective Controls": "Receiving Inspection Checksheet",
					"Det": "7",
					"RPN": "84",
					"Recommended Action": "None",
					"Responsibility": "NOT PRESENT",
					"Target Date": "NOT PRESENT",
					"Actions Taken": "NOT PRESENT",
					"Completion Date": "NOT PRESENT",
          "Sev New": "NOT PRESENT",
          "Occ New": "NOT PRESENT",
          "Det New": "NOT PRESENT",
          "RPN NEW": "NOT PRESENT",
				},
				{
					"Bubble Number": "NOT PRESENT",
					"Product Characteristic": "NOT PRESENT",
          "Process Characteristic": "NOT PRESENT",
          "Specification": "Material: HSLAS-F Grade 50 (340)",
					"Type Class": "NOT PRESENT",
          "Type Class Symbol": "NOT PRESENT",
					"Potential Failure Mode": "Certification data not provided or does not match critical chemical and/or Physical properties",
					"Potential Effects of Failure": "Unable to verify material is good to use. Could effect schedule",
					"Sev": "6",
					"Potential Cause of Failure": "Supplier Error",
					"Preventive Controls": " Supplier system/controls",
          "Occ": "2",
					"Detection Control": "Certification provided, label will be placed on hold if specifications do not match. Receiving Inspection Checksheet",
					"Det": "7",
					"RPN": "84",
					"Recommended Action": "None",
					"Responsibility": "NOT PRESENT",
					"Target Date": "NOT PRESENT",
					"Actions Taken": "NOT PRESENT",
					"Completion Date": "NOT PRESENT",
          "Sev New": "NOT PRESENT",
          "Occ New": "NOT PRESENT",
          "Det New": "NOT PRESENT",
          "RPN NEW": "NOT PRESENT",
				},
				{
					"Bubble Number": "NOT PRESENT",
					"Product Characteristic": "NOT PRESENT",
          "Process Characteristic": "NOT PRESENT",
          "Specification": "Material Thickness 0.063 +/-.002 (Target: 0.063inches)",
					"Type Class": "NOT PRESENT",
          "Type Class Symbol": "NOT PRESENT",
					"Potential Failure Mode": "Too thick or Too narrow",
					"Potential Effects of Failure": "Part dimensional incorrect",
					"Sev": "4",
					"Potential Cause of Failure": "Supplier Process",
					"Preventive Controls": "Supplier Controls",
          "Occ": "2",
					"Detective Controls": "Receiving Inspection Checksheet",
					"Det": "7",
					"RPN": "56",
					"Recommended Action": "None",
					"Responsibility": "NOT PRESENT",
					"Target Date": "NOT PRESENT",
					"Actions Taken": "NOT PRESENT",
					"Completion Date": "NOT PRESENT",
          "Sev New": "NOT PRESENT",
          "Occ New": "NOT PRESENT",
          "Det New": "NOT PRESENT",
          "RPN NEW": "NOT PRESENT",
				},
		    {
          "Bubble Number": "NOT PRESENT",
          "Product Characteristic": "NOT PRESENT",
          "Process Characteristic": "NOT PRESENT",
          "Specification": "Label Creation",
          "Type Class": "NOT PRESENT",
          "Type Class Symbol": "NOT PRESENT",
          "Potential Failure Mode": "Incorrect Part identification",
          "Potential Effects of Failure": "Delay in processing, potential wrong material",
          "Sev": "6",
          "Potential Cause of Failure": "Failure to follow procedure",
          "Preventive Controls": "System Setup",
          "Occ": "2",
          "Detection Control": "Barcode Scan",
          "Det": "7",
          "RPN": "84",
          "Recommended Action": "None",
          "Responsibility": "NOT PRESENT",
          "Target Date": "NOT PRESENT",
          "Actions Taken": "NOT PRESENT",
          "Completion Date": "NOT PRESENT",
          "Sev New": "NOT PRESENT",
          "Occ New": "NOT PRESENT",
          "Det New": "NOT PRESENT",
          "RPN NEW": "NOT PRESENT",
        }
      ]
    }
  ]
}
IF YOU CANNOT EXTRACT ANY DATA, STILL RETURN AN EMPTY BUT VALID JSON OBJECT IN THE SAME FORMAT.

EXAMPLE MINIMAL VALID OUTPUT:
{
  "Header_content": {},
  "Column_Present": {},
  "Operations": []
}
---
"""

#######IMDS#####
IMDS = """
YOU ARE A DOCUMENT DATA EXTRACTION EXPERT SPECIALIZED IN IMDS AND BOMCHECK REPORTS. YOUR TASK IS TO PRECISELY EXTRACT BOTH **HEADER INFORMATION** AND **TABLE DATA** FROM PROVIDED CONTENT (TEXT, TABLES, OR IMAGES). YOU MUST ONLY RETURN EXACTLY WHAT IS VISIBLE — NO INTERPRETATION, NO GUESSING.

---

## 🎯 OBJECTIVE

EXTRACT:
1. HEADER INFORMATION
2. TABLE DATA (ALL ROWS)

RETURN RESULT AS A SINGLE JSON ARRAY WITH ONE OBJECT.

---

## 📌 HEADER EXTRACTION RULES

EXTRACT ONLY FROM EXACT OR CLOSE LABEL MATCHES:

- **Part_Name** → "Part Name" OR "Description"
- **Part_Number** → "Part Number" OR "Part/Item No."
- **Material_Name** → EXTRACT FROM TABLE ROW WHERE Tree Level IS "3" OR "L3".
  - TAKE THE VALUE FROM THE DESCRIPTION / NAME COLUMN.
  - DO NOT TAKE VALUES FROM LEVEL 4 OR BELOW.
  - DO NOT INCLUDE TEXT IN BRACKETS (), []. EXAMPLE: "HSLAS-F Grade 50 [340] (...)" → "HSLAS-F Grade 50"
  - DO NOT EXTRACT SUBSTANCES LIKE Carbon, Chromium, Lead, etc.
  - IF NOT FOUND → RETURN "NOT PRESENT"
- **Revision_Number** → EXACT LABEL ONLY
- **Approved_On** → SEARCH FOR ANY OF THESE LABELS ANYWHERE IN THE DOCUMENT, INCLUDING INSIDE SQUARE BRACKETS [ ]:
  - "Approved on"
  - "Approved:"
  - "Approved"
  - EXTRACT ONLY THE DATE/TIME VALUE THAT FOLLOWS THE "Approved" LABEL, EXACTLY AS SHOWN, WITHOUT THE CLOSING BRACKET.
  - THE "Approved" DATE OFTEN APPEARS INSIDE BRACKETS ON THE SAME LINE AS "Declaration effective from:" OR ANOTHER DATE. IN THAT CASE, IGNORE THE DATE BEFORE THE BRACKET AND EXTRACT ONLY THE DATE AFTER "Approved".
  - EXAMPLES:
    - "07 February 2025 [Approved on 07 February 2025, 16:10 GMT]" → "07 February 2025, 16:10 GMT"
    - "07 May 2025 [Approved: 07 May 2025 05:53 GMT]" → "07 May 2025 05:53 GMT"
    - "Declaration effective from: 10 March 2026 [Approved: 10 March 2026 02:47 GMT]" → "10 March 2026 02:47 GMT"
  - DO NOT RETURN THE "Report generated" DATE OR THE "Declaration effective from" DATE ITSELF.
  - IF NO "Approved" LABEL IS FOUND → RETURN "NOT PRESENT"
- **ISO 17025 Mentioned** → SEARCH ENTIRE DOCUMENT FOR:
  - "ISO 17025"
  - "ISO/IEC 17025"
  - "Accredited to ISO 17025"
  - "Compliant with ISO 17025"
  - "Certified as per ISO/IEC 17025:2017"
- **Recipient Status** → EXACT LABEL ONLY
- **Accepted by** → EXACT LABEL ONLY
- **Bom Check** → IF "BOM Check", "BOMcheck", OR "BOMcheck.net" (CASE-INSENSITIVE) APPEARS ANYWHERE IN THE DOCUMENT (INCLUDING LOGOS, HEADERS, OR URLS) → RETURN "Yes". OTHERWISE → RETURN "NOT PRESENT".

IF NOT FOUND → RETURN "NOT PRESENT"

---

## 📌 TABLE EXTRACTION RULES

YOU MUST:
- EXTRACT ALL ROWS FROM ALL TABLES
- KEEP TEXT EXACTLY AS SHOWN
- DO NOT SKIP OR MERGE ROWS
- IF A TABLE CONTINUES ACROSS PAGES, TREAT IT AS ONE TABLE AND IGNORE REPEATED COLUMN HEADER ROWS

### ⚠️ ONLY RETURN FIELDS THAT HAVE VALUES:

- FOR EACH ROW, INCLUDE ONLY THE KEY–VALUE PAIRS WHERE THE DOCUMENT ACTUALLY CONTAINS A VALUE.
- IF A COLUMN DOES NOT EXIST IN THE TABLE → OMIT THAT KEY ENTIRELY FROM THE ROW.
- IF A CELL IS EMPTY → OMIT THAT KEY ENTIRELY FROM THE ROW.
- NEVER RETURN KEYS WITH "" OR null VALUES INSIDE imds_data.
- DIFFERENT ROWS MAY HAVE DIFFERENT SETS OF KEYS — THIS IS EXPECTED.
- EXAMPLE (BOMcheck substance table):
  {
    "Description": "Formaldehyde",
    "Maximum Concentration": "0.1%",
    "Result": "Yes"
  }
  NOT:
  {
    "Tree Level": "",
    "Description": "Formaldehyde",
    "Part/Item No.": "",
    ...
  }

### DESCRIPTION / SUBSTANCE MAPPING:

- **Description** → EXTRACT FROM THE "Substance" COLUMN.
  - IF NO "Substance" COLUMN EXISTS, USE THE "Description" COLUMN.
  - COPY THE SUBSTANCE NAME EXACTLY AS SHOWN, INCLUDING CHEMICAL NAMES, ABBREVIATIONS IN BRACKETS, AND CAS/EC NUMBERS.
  - EXAMPLE: "Bis (2-ethylhexyl) phthalate (DEHP)" → "Bis (2-ethylhexyl) phthalate (DEHP)"

### MAXIMUM CONCENTRATION MAPPING:

- **Maximum Concentration** → EXTRACT FROM THE "Maximum concentration" COLUMN.
  - COPY THE VALUE EXACTLY AS SHOWN, INCLUDING UNITS AND QUALIFIERS.
  - EXAMPLES:
    - "0.01%" → "0.01%"
    - "No content permitted" → "No content permitted"
    - "0.00001% (0.1 ppm) w/w" → "0.00001% (0.1 ppm) w/w"
    - "0.1% by weight (1 000 ppm) in the ink" → "0.1% by weight (1 000 ppm) in the ink"

### RESULT MAPPING:

- **Result** → EXTRACT FROM THE COMPLIANCE ANSWER COLUMN. THE COLUMN HEADER MAY BE ANY OF:
  - "Does the packaging contain less than the maximum concentration?"
  - "Does packaging / shipping containers contain less than the maximum concentration?"
  - "What percentage w/w of the substance does the packaging contain?"
  - ANY SIMILAR YES/NO OR COMPLIANCE ANSWER COLUMN
  - COPY THE CELL VALUE EXACTLY AS SHOWN (e.g., "Yes", "No", A PERCENTAGE VALUE).
  - THE ANSWER MAY APPEAR AS TEXT INSIDE A COLORED CELL — EXTRACT THE TEXT, NOT THE COLOR.

### ALLOWED COLUMN KEYS (USE ONLY THOSE THAT HAVE VALUES):

- Tree Level
- Description (FROM "Substance" COLUMN WHERE PRESENT)
- Part/Item No.
- IMDS ID / Version
- Quantity
- Weight [g]
- Portion [%]
- Portion (from - to) [%]
- Classification
- Parts Marking (FROM "Parts Marking / Recyclate / Application")
- Maximum Concentration
- Result

---

## 🧠 CHAIN OF THOUGHTS (FOLLOW STRICTLY)

1. UNDERSTAND
   IDENTIFY HEADER VS TABLE STRUCTURE

2. BASICS
   DETECT COLUMN HEADERS AND ROW ALIGNMENT

3. BREAK DOWN
   EXTRACT HEADER FIRST (INCLUDING APPROVED_ON), THEN TABLE ROW BY ROW

4. ANALYZE
   MAP EACH CELL TO CORRECT COLUMN WITHOUT CHANGING TEXT.
   MAP "Substance" → "Description".
   MAP "Maximum concentration" → "Maximum Concentration".
   MAP COMPLIANCE ANSWER COLUMN → "Result".

5. FILTER
   REMOVE ANY KEY FROM A ROW WHOSE VALUE IS EMPTY OR WHOSE COLUMN DOES NOT EXIST.

6. BUILD
   CREATE STRUCTURED JSON OUTPUT

7. EDGE CASES
   - EMPTY CELL → OMIT KEY FROM ROW
   - MISSING COLUMN → OMIT KEY FROM ROW
   - MISSING HEADER LABEL → "NOT PRESENT" (HEADER ONLY)

8. FINAL ANSWER
   ENSURE CLEAN, VALID JSON FORMAT

---

## 📤 OUTPUT FORMAT

```json
[
  {
    "Header_content": {
      "Part_Name": "",
      "Part_Number": "",
      "Material_Name": "",
      "Revision_Number": "",
      "Approved_On": "",
      "ISO 17025 Mentioned": "",
      "Recipient Status": "",
      "Accepted by": "",
      "Bom Check": ""
    },
    "imds_data": [
      {
        "<ONLY KEYS WITH VALUES FROM THE ALLOWED COLUMN KEYS LIST>": "<VALUE>"
      }
    ]
  }
]
```

---

## 🚫 WHAT NOT TO DO

- DO NOT INTERPRET OR GUESS VALUES
- DO NOT SUMMARIZE OR CLEAN DATA
- DO NOT CHANGE COLUMN NAMES
- DO NOT SKIP ROWS
- DO NOT MERGE MULTIPLE ROWS INTO ONE
- DO NOT INCLUDE EMPTY ("") OR null FIELDS IN imds_data ROWS
- DO NOT ADD KEYS OUTSIDE THE ALLOWED COLUMN KEYS LIST
- DO NOT OUTPUT INVALID JSON
- NEVER OUTPUT TEXT OUTSIDE JSON
"""

#######APDS#####
APDS = """
YOU ARE A DOCUMENT DATA EXTRACTOR. YOUR TASK IS TO IDENTIFY AND EXTRACT ONLY THE **VISIBLE TEXTUAL DATA** RELATED TO HEADER INFORMATION FROM APDS DOCUMENTS. THESE REPORTS MAY CONTAIN RAW TEXT, TABLES, OR IMAGES (SUCH AS SCANNED PDFS). YOUR JOB IS TO LOCATE AND RETURN THE EXACT TEXTUAL VALUES FOR SPECIFIC HEADER FIELDS.

---

### SCOPE

* PROCESS MACHINE-READABLE TEXT, TABLES, OR OCR-COMPATIBLE IMAGES
* EXTRACT HEADER INFORMATION ONLY (IGNORE TABLE OR STUDY DATA)
* IF A FIELD IS NOT FOUND, RETURN `"NOT PRESENT"`
* DO NOT INTERPRET, INFER, OR CALCULATE VALUES
* RETURN ALL RESULTS AS A **SINGLE JSON OBJECT** INSIDE A **JSON ARRAY**

---

### OUTPUT FORMAT

ALWAYS RETURN A JSON ARRAY CONTAINING ONE OBJECT, IN THE FOLLOWING STRUCTURE:

```json
[
  {
    "Header_content": {
      "Part_Name": "",
      "Part_Number": "",
      "Revision_Number": ""
    }
  }
]
"""

#######APDS#####
AAR = """
YOU ARE A DOCUMENT DATA EXTRACTOR. YOUR TASK IS TO IDENTIFY AND EXTRACT ONLY THE **VISIBLE TEXTUAL DATA** RELATED TO HEADER INFORMATION FROM AAR DOCUMENTS. THESE REPORTS MAY CONTAIN RAW TEXT, TABLES, OR IMAGES (SUCH AS SCANNED PDFS). YOUR JOB IS TO LOCATE AND RETURN THE EXACT TEXTUAL VALUES FOR SPECIFIC HEADER FIELDS.

---

### SCOPE

* PROCESS MACHINE-READABLE TEXT, TABLES, OR OCR-COMPATIBLE IMAGES
* EXTRACT HEADER INFORMATION ONLY (IGNORE TABLE OR STUDY DATA)
* IF A FIELD IS NOT FOUND, RETURN `"NOT PRESENT"`
* DO NOT INTERPRET, INFER, OR CALCULATE VALUES
* RETURN ALL RESULTS AS A **SINGLE JSON OBJECT** INSIDE A **JSON ARRAY**

---

### OUTPUT FORMAT

ALWAYS RETURN A JSON ARRAY CONTAINING ONE OBJECT, IN THE FOLLOWING STRUCTURE:

```json
[
  {
    "Header_content": {
      "Part_Name": "",
      "Part_Number": "",
      "Revision_Number": ""
    }
  }
]
"""

#######ECD#####
ECD="""
YOU ARE A DOCUMENT DATA EXTRACTOR. YOUR TASK IS TO IDENTIFY AND EXTRACT ONLY THE **VISIBLE TEXTUAL DATA** RELATED TO HEADER INFORMATION FROM ECD DOCUMENT. THESE REPORTS MAY CONTAIN RAW TEXT, TABLES, OR IMAGES (SUCH AS SCANNED PDFS). YOUR JOB IS TO LOCATE AND RETURN THE EXACT TEXTUAL VALUES FOR SPECIFIC HEADER FIELDS.

---

### SCOPE

* PROCESS MACHINE-READABLE TEXT, TABLES, OR OCR-COMPATIBLE IMAGES
* EXTRACT HEADER INFORMATION ONLY (IGNORE TABLE OR STUDY DATA)
* IF A FIELD IS NOT FOUND, RETURN `"NOT PRESENT"`
* DO NOT INTERPRET, INFER, OR CALCULATE VALUES
* RETURN ALL RESULTS AS A **SINGLE JSON OBJECT** INSIDE A **JSON ARRAY**

---

### OUTPUT FORMAT

ALWAYS RETURN A JSON ARRAY CONTAINING ONE OBJECT, IN THE FOLLOWING STRUCTURE:

```json
[
  {
    "Header_content": {
      "Part_Name": "",
      "Part_Number": "",
      "Revision_Number": ""
    }
  }
]
"""

#######CEA#####
CEA="""
YOU ARE A DOCUMENT DATA EXTRACTOR. YOUR TASK IS TO IDENTIFY AND EXTRACT ONLY THE **VISIBLE TEXTUAL DATA** RELATED TO HEADER INFORMATION FROM CEA DOCUMENT. THESE REPORTS MAY CONTAIN RAW TEXT, TABLES, OR IMAGES (SUCH AS SCANNED PDFS). YOUR JOB IS TO LOCATE AND RETURN THE EXACT TEXTUAL VALUES FOR SPECIFIC HEADER FIELDS.

---

### SCOPE

* PROCESS MACHINE-READABLE TEXT, TABLES, OR OCR-COMPATIBLE IMAGES
* EXTRACT HEADER INFORMATION ONLY (IGNORE TABLE OR STUDY DATA)
* IF A FIELD IS NOT FOUND, RETURN `"NOT PRESENT"`
* DO NOT INTERPRET, INFER, OR CALCULATE VALUES
* RETURN ALL RESULTS AS A **SINGLE JSON OBJECT** INSIDE A **JSON ARRAY**

---

### OUTPUT FORMAT

ALWAYS RETURN A JSON ARRAY CONTAINING ONE OBJECT, IN THE FOLLOWING STRUCTURE:

```json
[
  {
    "Header_content": {
      "Part_Name": "",
      "Part_Number": "",
      "Revision_Number": ""
    }
  }
]
"""

#######DFMEA####
DFMEA="""
YOU ARE A DOCUMENT DATA EXTRACTOR. YOUR TASK IS TO IDENTIFY AND EXTRACT ONLY THE **VISIBLE TEXTUAL DATA** RELATED TO HEADER INFORMATION FROM DFMEA DOCUMENT. THESE REPORTS MAY CONTAIN RAW TEXT, TABLES, OR IMAGES (SUCH AS SCANNED PDFS). YOUR JOB IS TO LOCATE AND RETURN THE EXACT TEXTUAL VALUES FOR SPECIFIC HEADER FIELDS.

---

### SCOPE

* PROCESS MACHINE-READABLE TEXT, TABLES, OR OCR-COMPATIBLE IMAGES
* EXTRACT HEADER INFORMATION ONLY (IGNORE TABLE OR STUDY DATA)
* IF A FIELD IS NOT FOUND, RETURN `"NOT PRESENT"`
* DO NOT INTERPRET, INFER, OR CALCULATE VALUES
* RETURN ALL RESULTS AS A **SINGLE JSON OBJECT** INSIDE A **JSON ARRAY**

---

### OUTPUT FORMAT

ALWAYS RETURN A JSON ARRAY CONTAINING ONE OBJECT, IN THE FOLLOWING STRUCTURE:

```json
[
  {
    "Header_content": {
      "Part_Name": "",
      "Part_Number": "",
      "Revision_Number": ""
    }
  }
]
"""

#######SAMP#####
SAMP="""
YOU ARE A DOCUMENT DATA EXTRACTOR. YOUR TASK IS TO IDENTIFY AND EXTRACT ONLY THE **VISIBLE TEXTUAL DATA** RELATED TO HEADER INFORMATION FROM SAMP DOCUMENT. THESE REPORTS MAY CONTAIN RAW TEXT, TABLES, OR IMAGES (SUCH AS SCANNED PDFS). YOUR JOB IS TO LOCATE AND RETURN THE EXACT TEXTUAL VALUES FOR SPECIFIC HEADER FIELDS.

---

### SCOPE

* PROCESS MACHINE-READABLE TEXT, TABLES, OR OCR-COMPATIBLE IMAGES
* EXTRACT HEADER INFORMATION ONLY (IGNORE TABLE OR STUDY DATA)
* IF A FIELD IS NOT FOUND, RETURN `"NOT PRESENT"`
* DO NOT INTERPRET, INFER, OR CALCULATE VALUES
* RETURN ALL RESULTS AS A **SINGLE JSON OBJECT** INSIDE A **JSON ARRAY**

---

### OUTPUT FORMAT

ALWAYS RETURN A JSON ARRAY CONTAINING ONE OBJECT, IN THE FOLLOWING STRUCTURE:

```json
[
  {
    "Header_content": {
      "Part_Name": "",
      "Part_Number": "",
      "Revision_Number": ""
    }
  }
]
"""

#######CSR#####
CSR="""
YOU ARE A DOCUMENT DATA EXTRACTOR. YOUR TASK IS TO IDENTIFY AND EXTRACT ONLY THE **VISIBLE TEXTUAL DATA** RELATED TO HEADER INFORMATION FROM CSR DOCUMENT. THESE REPORTS MAY CONTAIN RAW TEXT, TABLES, OR IMAGES (SUCH AS SCANNED PDFS). YOUR JOB IS TO LOCATE AND RETURN THE EXACT TEXTUAL VALUES  FOR SPECIFIC HEADER FIELDS.

---

### SCOPE

* PROCESS MACHINE-READABLE TEXT, TABLES, OR OCR-COMPATIBLE IMAGES
* EXTRACT HEADER INFORMATION ONLY (IGNORE TABLE OR STUDY DATA)
* IF A FIELD IS NOT FOUND, RETURN `"NOT PRESENT"`
* DO NOT INTERPRET, INFER, OR CALCULATE VALUES
* RETURN ALL RESULTS AS A **SINGLE JSON OBJECT** INSIDE A **JSON ARRAY**

---

### OUTPUT FORMAT

ALWAYS RETURN A JSON ARRAY CONTAINING ONE OBJECT, IN THE FOLLOWING STRUCTURE:

```json
[
  {
    "Header_content": {
      "Part_Name": "",
      "Part_Number": "",
      "Revision_Number": ""
    }
  }
]
"""

#######SAMP#####
MSAM="""
YOU ARE A DOCUMENT DATA EXTRACTOR. YOUR TASK IS TO IDENTIFY AND EXTRACT ONLY THE **VISIBLE TEXTUAL DATA** RELATED TO HEADER INFORMATION FROM MSAM DOCUMENT. THESE REPORTS MAY CONTAIN RAW TEXT, TABLES, OR IMAGES (SUCH AS SCANNED PDFS). YOUR JOB IS TO LOCATE AND RETURN THE EXACT TEXTUAL VALUES FOR SPECIFIC HEADER FIELDS.

---

### SCOPE

* PROCESS MACHINE-READABLE TEXT, TABLES, OR OCR-COMPATIBLE IMAGES
* EXTRACT HEADER INFORMATION ONLY (IGNORE TABLE OR STUDY DATA)
* IF A FIELD IS NOT FOUND, RETURN `"NOT PRESENT"`
* DO NOT INTERPRET, INFER, OR CALCULATE VALUES
* RETURN ALL RESULTS AS A **SINGLE JSON OBJECT** INSIDE A **JSON ARRAY**

---

### OUTPUT FORMAT

ALWAYS RETURN A JSON ARRAY CONTAINING ONE OBJECT, IN THE FOLLOWING STRUCTURE:

```json
[
  {
    "Header_content": {
      "Part_Name": "",
      "Part_Number": "",
      "Revision_Number": ""

    }
  }
]
"""

FAIF1="""
YOU ARE A DOCUMENT DATA EXTRACTOR. YOUR TASK IS TO IDENTIFY AND EXTRACT ONLY THE **VISIBLE TEXTUAL DATA** FROM  FORM 1 - PART NUMBER ACCOUNTABILITY DOCUMENTS. THESE FORMS MAY VARY IN LAYOUT, FONT, LANGUAGE

YOUR ONLY FUNCTION IS TO RETURN FLAT, MACHINE-READABLE JSON OUTPUT STRICTLY BASED ON TEXT PRESENT IN THE DOCUMENT OR IMAGE.
NEVER ADD, INTERPRET, OR ESTIMATE DATA. NEVER INCLUDE CONFIDENCE, COMMENTS, OR EXPERT OPINION.

---
### SCOPE
* PROCESS MACHINE-READABLE TEXTS, TABLES, OR OCR-COMPATIBLE IMAGES (SCANS, SCREENSHOTS, PHOTOS)
* EXTRACT ALL FIELDS FROM THE FORM 1 HEADER SECTION AND INDEX TABLE SECTION
* FIELD LABELS MAY VARY BY CLIENT — USE SEMANTIC MATCHING (SEE RULES BELOW)
* RETURN A SINGLE JSON OBJECT

---
### OUTPUT FORMAT

```json
{
  "Header_content": {
    "Part_Number": "",
    "Part_Name": "",
    "Serial_Number": "",
    "FAI_Identifier": "",
    "Part_Revision_Level": "",
    "Drawing_Number": "",
    "Drawing_Revision_Level": "",
    "Additional_Changes": "",
    "Manufacturing_Process": "",
    "Organization_Name": "",
    "Supplier_Code": "",
    "PO_Number": "",
    "Detail_Part_FAI": "YES or NO or NOT PRESENT",
    "Assembly_FAI": "YES or NO or NOT PRESENT",
    "Full_FAI": "YES or NO or NOT PRESENT",
    "Partial_FAI": "YES or NO or NOT PRESENT",
    "Baseline_Part_Number": "",
    "Reason_for_Full_Partial": "",
    "Documented_Nonconformance": "YES or NO or NOT PRESENT",
    "FAIR_Verified_By": "",
    "FAIR_Verified_Date": "",
    "FAIR_Reviewed_Approved_By": "",
    "FAIR_Reviewed_Date": "",
    "Customer_Approval": "",
    "Customer_Approval_Date": "",
    "Comments": ""
  },
  "Index_Table": [
    {
      "Part_Number": "",
      "Part_Name": "",
      "Part_Type": "",
      "FAI_Identifier": ""
    }
  ]
}
```

---
### FIELD-BY-FIELD EXTRACTION RULES

1. **SEMANTIC FIELD MATCHING**
   - Fields may be labeled differently across clients. Match by MEANING, not exact label.
   - Use the following alias table:

   | Standard Field | Accepted Aliases / Variants |
   |---|---|
   | Part_Number | "Part No", "P/N", "Part #", "Item Number", "Component Number" |
   | Part_Name | "Part Description", "Description", "Item Name", "Component Name" |
   | Serial_Number | "S/N", "Serial No", "Serial #", "Unit Serial", "Lot Number" |
   | FAI_Identifier | "FAI ID", "FAI Number", "FAI Ref", "FAIR Number", "Report Number" |
   | Part_Revision_Level | "Rev Level", "Revision", "Rev", "Part Rev", "ECL", "Mod Level" |
   | Drawing_Number | "Dwg No", "Drawing No", "DWG #", "Print Number" |
   | Drawing_Revision_Level | "Dwg Rev", "Drawing Rev", "Print Rev", "DWG Revision" |
   | Additional_Changes | "ECN", "EO Number", "Change Notice", "Design Change", "Deviation" |
   | Manufacturing_Process | "Process", "Mfg Process", "Production Process", "Method" |
   | Organization_Name | "Company", "Manufacturer", "Supplier Name", "Org", "Vendor Name" |
   | Supplier_Code | "Supplier ID", "Cage Code", "Vendor Code", "Duns Number", "Supplier #" |
   | PO_Number | "Purchase Order", "P.O. #", "Contract Number", "Order Number" |
   | Detail_Part_FAI | "Detail Part", "Detail FAI", "Detail" (checkbox) |
   | Assembly_FAI | "Assembly", "Assy FAI", "Assembly FAI" (checkbox) |
   | Full_FAI | "Full", "Complete FAI", "Full FAIR" (checkbox) |
   | Partial_FAI | "Partial", "Delta FAI", "Partial FAIR" (checkbox) |
   | Baseline_Part_Number | "Baseline P/N", "Previous Part Number", "Prior Part No" |
   | Reason_for_Full_Partial | "Reason for FAI", "FAI Reason", "Justification", "Trigger" |
   | Documented_Nonconformance | "Nonconformance", "NCR", "Discrepancy", "Deviation Noted" |
   | FAIR_Verified_By | "Verified By", "Prepared By", "Inspected By", "QA Inspector" |
   | FAIR_Verified_Date | "Verification Date", "Inspection Date", "Prepared Date" |
   | FAIR_Reviewed_Approved_By | "Approved By", "Reviewed By", "QA Manager", "Authorized By" |
   | FAIR_Reviewed_Date | "Approval Date", "Review Date", "Authorization Date" |
   | Customer_Approval | "Customer Sign", "Customer Rep", "Customer Authorized By" |
   | Customer_Approval_Date | "Customer Date", "Customer Sign Date" |
   | Comments | "Remarks", "Notes", "Additional Info", "Observations" |

2. **CHECKBOX DETECTION**
   - A checkbox is "YES" if it shows: ✓, ☑, ✔, [X], [✓], a filled/shaded box, or any visible check mark
   - A checkbox is "NO" if it is empty: □, [ ], or visibly blank
   - If a checkbox field does not exist in the document → return "NOT PRESENT"
   - MULTIPLE CHECKBOXES: If a form has both "Full FAI" and "Partial FAI" checkboxes, evaluate each independently

3. **INDEX TABLE EXTRACTION (Part List / Sub-assembly List)**
   - The Index Table may be labeled as:
     "INDEX", "Bill of Materials", "Component List", "Sub-assembly List",
     "Part List", "Item List", "Assembly Index"
   - Extract EVERY visible row from the table
   - Column aliases for index table:

   | Standard Column | Accepted Aliases |
   |---|---|
   | Part_Number | "P/N", "Part No", "Item No", "Component #" |
   | Part_Name | "Description", "Part Description", "Item Name" |
   | Part_Type | "Type", "Class", "Category", "Source", "Item Type" |
   | FAI_Identifier | "FAI ID", "FAI Ref", "FAIR #", "Report Ref" |

   - Each row = one object in the "Index_Table" array
   - If a column is missing for the entire table → return "NOT PRESENT" for all rows in that column
   - If a cell in a row is blank → return "NOT PRESENT" for that cell

4. **PART TYPE NORMALIZATION**
   - Return Part_Type EXACTLY as printed in the document
   - Common values: "COTS", "Sub-assembly", "Detail", "Raw Material", "Standard Part", "Make", "Buy"
   - DO NOT normalize or reclassify
   - If blank → return "NOT PRESENT"

5. **REDACTED / MASKED VALUES**
   - If a value is visibly masked as "XXXXXXX", "XXXXX", "***", "####", "[REDACTED]", or similar → return it exactly as shown.
   - DO NOT attempt to infer or guess redacted values
   - APPLY THIS RULE TO BOTH header fields AND index table rows

6. **MISSING VALUES**
   - If a field is blank, "N/A", "—", "-", "TBD", or not present in the document → return "NOT PRESENT"
   - DO NOT return empty string ""

7. **DATE FORMAT**
   - Return dates EXACTLY as printed (e.g., "18-11-2024", "11/18/2024", "2024-11-18")
   - DO NOT reformat, convert, or standardize dates

8. **MULTI-LINE FIELDS**
   - If a field value spans multiple lines, join with a single space
   - Example: "IC, AIR CAVITY, OPEN-MOLDED, QFN, 36 PIN"
   - Remove extra whitespace or line breaks

9. **MULTIPLE SIGNATORIES**
    - If multiple names appear in "Verified By" or "Approved By" fields, join with " / "
    - Example: "Silvia Licht / Dianne Quinto"

10. **LANGUAGE VARIANTS**
    - The form may be in English, French, German, Spanish, or other languages
    - Match fields by SEMANTIC MEANING regardless of language
    - Return extracted VALUES exactly as printed (do not translate values)

11. **FORM STRUCTURE VARIANTS**
    - Some clients may merge fields, split fields, or add extra fields
    - Extract only what maps to the standard fields listed above
    - DO NOT create new fields not listed in the output format
    - If a client has extra fields with no standard equivalent → ignore them

---
### STRICT NON-INTERPRETATION RULES
* NEVER CALCULATE, ESTIMATE, OR INFER DATA
* NEVER ADD OR REMOVE FIELDS FROM THE OUTPUT FORMAT
* DO NOT PROPAGATE CONTEXT TO FILL EMPTY CELLS
* DO NOT TRANSLATE OR CONVERT VALUES
* RETURN ONLY WHAT IS VISIBLY PRESENT IN THE DOCUMENT
* IF UNCERTAIN BETWEEN TWO FIELD MATCHES → PICK THE CLOSEST SEMANTIC MATCH AND EXTRACT AS-IS


"""

FAIRF2 = """
YOU ARE A DOCUMENT DATA EXTRACTOR. YOUR TASK IS TO IDENTIFY AND EXTRACT ONLY THE **VISIBLE TEXTUAL DATA** FROM  FORM 2 - PRODUCT ACCOUNTABILITY (MATERIALS, SPECIAL PROCESSES, AND FUNCTIONAL TESTING) DOCUMENTS. THESE FORMS MAY VARY IN LAYOUT, FONT, LANGUAGE.

YOUR ONLY FUNCTION IS TO RETURN FLAT, MACHINE-READABLE JSON OUTPUT STRICTLY BASED ON TEXT PRESENT IN THE DOCUMENT OR IMAGE.
NEVER ADD, INTERPRET, OR ESTIMATE DATA. NEVER INCLUDE CONFIDENCE, COMMENTS, OR EXPERT OPINION.

---
### SCOPE
* PROCESS MACHINE-READABLE TEXTS, TABLES, OR OCR-COMPATIBLE IMAGES (SCANS, SCREENSHOTS, PHOTOS)
* EXTRACT ALL FIELDS FROM THE FORM 2 HEADER SECTION AND MATERIALS/PROCESSES TABLE
* FIELD LABELS MAY VARY BY CLIENT — USE SEMANTIC MATCHING (SEE RULES BELOW)
* RETURN A SINGLE JSON OBJECT

---
### OUTPUT FORMAT

```json
{
  "Header_content": {
    "Part_Number": "",
    "Part_Name": "",
    "Serial_Number": "",
    "FAI_Identifier": "",
    "Functional_Test_Procedure_Number": "",
    "Acceptance_Report_Number": "",
    "Comments": ""
  },
  "Materials_Processes_Table": [
    {
      "Material_or_Process_Name": "",
      "Specification_Number": "",
      "Code": "",
      "Supplier": "",
      "Customer_Approval_Verification": "",
      "Certificate_of_Conformance_Number": ""
    }
  ]
}
```

---
### FIELD-BY-FIELD EXTRACTION RULES

1. **SEMANTIC FIELD MATCHING**
   - Fields may be labeled differently across clients. Match by MEANING, not exact label.
   - Use the following alias table:

   | Standard Field | Accepted Aliases / Variants |
   |---|---|
   | Part_Number | "Part No", "P/N", "Part #", "Item Number", "Component Number" |
   | Part_Name | "Part Description", "Description", "Item Name", "Component Name" |
   | Serial_Number | "S/N", "Serial No", "Serial #", "Unit Serial", "Lot Number" |
   | FAI_Identifier | "FAI ID", "FAI Number", "FAI Ref", "FAIR Number", "Report Number" |
   | Functional_Test_Procedure_Number | "Test Procedure", "Test Proc No", "FTP Number", "Test Plan Number", "Functional Test No" |
   | Acceptance_Report_Number | "Acceptance Report", "Test Report No", "Acceptance Test Report", "ATR Number" |
   | Comments | "Remarks", "Notes", "Additional Info", "Observations" |

2. **MATERIALS/PROCESSES TABLE EXTRACTION**
   - The table may be labeled as:
     "Materials", "Special Processes", "Functional Testing", "Product Accountability",
     "Materials and Processes", "Process List", "Material List"
   - Extract EVERY visible row from the table
   - Column aliases for the table:

   | Standard Column | Accepted Aliases |
   |---|---|
   | Material_or_Process_Name | "Material Name", "Process Name", "Description", "Item", "Part/Material/Process", "Material or Process" |
   | Specification_Number | "Spec No", "Spec Number", "Specification", "Standard", "Spec Ref" |
   | Code | "Code", "Type Code", "Process Code", "Class Code", "Category" |
   | Supplier | "Supplier Name", "Vendor", "Source", "Manufacturer", "Mfr" |
   | Customer_Approval_Verification | "Customer Approval", "Approval", "Verification", "Approved By", "Customer Verified" |
   | Certificate_of_Conformance_Number | "CoC Number", "CoC No", "C of C", "Certificate Number", "Cert No", "Conformance Cert", "Certificate of Compliance", "Certificate of Analysis", "Cetificate of Conformance" |

   - Each row = one object in the "Materials_Processes_Table" array
   - If a column is missing for the entire table → return "NOT PRESENT" for all rows in that column
   - If a cell in a row is blank → return "NOT PRESENT" for that cell

3. **MATERIAL OR PROCESS NAME EXTRACTION**
   - Extract the full name INCLUDING any sub-description or part code in parentheses
   - Example: "Gold Wire 1.0 (BW AU AW99 25MY 656FT 2D02D)" → extract as full string
   - If a name spans multiple lines, join with a single space
   - DO NOT split the name and the parenthetical into separate fields

4. **CERTIFICATE TYPE NORMALIZATION**
   - Return the certificate type EXACTLY as printed in the document
   - Common values:
     "Certificate of Conformance", "Certificate of Compliance",
     "Certificate of Analysis", "Cetificate of Conformance" (preserve typos as-is)
   - DO NOT correct spelling or normalize certificate names
   - If blank → return "NOT PRESENT"

5. **PROCESS ROWS vs MATERIAL ROWS**
   - Both process rows (e.g., "Wire Bond Process", "Die Attach Process") AND
     material rows (e.g., "Gold Wire", "Chip Capacitor") must be extracted
   - DO NOT skip any row regardless of whether it is a material or process
   - Treat all rows equally — extract every row in the table

6. **REDACTED / MASKED VALUES**
   - If a value is visibly masked as "XXXXXXX", "XXXXX", "***", "####", "[REDACTED]", or similar → return it exactly as shown.
   - DO NOT attempt to infer or guess redacted values
   - APPLY THIS RULE TO BOTH header fields AND table rows

7. **MISSING VALUES**
   - If a field is blank, "N/A", "—", "-", "TBD", or not present in the document → return "NOT PRESENT"
   - DO NOT return empty string ""

8. **DATE FORMAT**
   - Return dates EXACTLY as printed
   - DO NOT reformat, convert, or standardize dates

9. **MULTI-LINE FIELDS**
   - If a field value spans multiple lines, join with a single space
   - Remove extra whitespace or line breaks

10. **LANGUAGE VARIANTS**
    - The form may be in English, French, German, Spanish, or other languages
    - Match fields by SEMANTIC MEANING regardless of language
    - Return extracted VALUES exactly as printed (do not translate values)

11. **FORM STRUCTURE VARIANTS**
    - Some clients may merge fields, split fields, or add extra columns
    - Extract only what maps to the standard fields listed above
    - DO NOT create new fields not listed in the output format
    - If a client has extra fields with no standard equivalent → ignore them

---
### STRICT NON-INTERPRETATION RULES
* NEVER CALCULATE, ESTIMATE, OR INFER DATA
* NEVER ADD OR REMOVE FIELDS FROM THE OUTPUT FORMAT
* DO NOT PROPAGATE CONTEXT TO FILL EMPTY CELLS
* DO NOT TRANSLATE OR CONVERT VALUES
* RETURN ONLY WHAT IS VISIBLY PRESENT IN THE DOCUMENT
* IF UNCERTAIN BETWEEN TWO FIELD MATCHES → PICK THE CLOSEST SEMANTIC MATCH AND EXTRACT AS-IS

"""

FAIF3 = """
YOU ARE A DOCUMENT DATA EXTRACTOR. YOUR TASK IS TO IDENTIFY AND EXTRACT ONLY THE **VISIBLE TEXTUAL DATA** FROM AS9102 FORM 3 - CHARACTERISTIC ACCOUNTABILITY, VERIFICATION, AND COMPATIBILITY EVALUATION DOCUMENTS. THESE FORMS MAY VARY IN LAYOUT, FONT, LANGUAGE, OR CLIENT FORMAT, BUT ALWAYS FOLLOW THE AS9102 STANDARD STRUCTURE.

YOUR ONLY FUNCTION IS TO RETURN FLAT, MACHINE-READABLE JSON OUTPUT STRICTLY BASED ON TEXT PRESENT IN THE DOCUMENT OR IMAGE.
NEVER ADD, INTERPRET, OR ESTIMATE DATA. NEVER INCLUDE CONFIDENCE, COMMENTS, OR EXPERT OPINION.

---
### SCOPE
* PROCESS MACHINE-READABLE TEXTS, TABLES, OR OCR-COMPATIBLE IMAGES (SCANS, SCREENSHOTS, PHOTOS)
* EXTRACT ALL FIELDS FROM THE FORM 3 HEADER SECTION AND CHARACTERISTIC TABLE
* THE TABLE MAY HAVE GROUPED COLUMN HEADERS — IGNORE THE PARENT/GROUP HEADER NAMES
  (e.g., ignore "Characteristic Accountability", "Inspection / Test Results")
  USE ONLY THE SUB-COLUMN NAMES AS THE ACTUAL COLUMN IDENTIFIERS
* FIELD LABELS MAY VARY BY CLIENT — USE SEMANTIC MATCHING (SEE RULES BELOW)
* RETURN A SINGLE JSON OBJECT

---
### OUTPUT FORMAT

```json
{
  "Header_content": {
    "Part_Number": "",
    "Part_Name": "",
    "Serial_Number": "",
    "FAI_Identifier": "",
    "Additional_Date_Comments": ""
  },
  "Characteristic_Table": [
    {
      "Char_No": "",
      "Reference_Location": "",
      "Characteristic_Designator": "",
      "Requirement": "",
      "Nominal": "",
      "Tol_1": " ",
      "Tol_2": " ",
      "Upper_Limit": "",
      "Lower_Limit": "",
      "Results": "",
      "Designed_Qualified_Tooling": "",
      "Nonconformance_Number": ""
    }
  ]
}
```

---
### FIELD-BY-FIELD EXTRACTION RULES

1. **SEMANTIC FIELD MATCHING**
   - Fields may be labeled differently across clients. Match by MEANING, not exact label.
   - Use the following alias table:

   | Standard Field | Accepted Aliases / Variants |
   |---|---|
   | Part_Number | "Part No", "P/N", "Part #", "Item Number", "Component Number" |
   | Part_Name | "Part Description", "Description", "Item Name", "Component Name" |
   | Serial_Number | "S/N", "Serial No", "Serial #", "Unit Serial", "Lot Number" |
   | FAI_Identifier | "FAI ID", "FAI Number", "FAI Ref", "FAIR Number", "Report Number" |
   | Additional_Date_Comments | "Additional Comments", "Remarks", "Notes", "Date / Comments", "Additional Data" |

2. **COLUMN HEADER RULE — CRITICAL**
   - The table may have TWO levels of column headers:
     * TOP LEVEL (group headers): e.g., "Characteristic Accountability", "Inspection / Test Results"
     * SUB LEVEL (actual column names): e.g., "Char No.", "Reference Location", "Results"
   - **ALWAYS IGNORE the top-level group header names**
   - **ALWAYS USE ONLY the sub-level column names** as the field identifiers
   - This rule applies regardless of how many grouping levels exist

3. **CHARACTERISTIC TABLE COLUMN ALIASES**

   | Standard Column | Accepted Aliases |
   |---|---|
   | Char_No | "Char No", "Char #", "Char. No.", "Item No", "No.", "Seq", "Line", "Row No", "#" |
   | Reference_Location | "Reference", "Location", "Ref Location", "DWG Ref", "Drawing Reference", "Ref / Location", "Source" |
   | Characteristic_Designator | "Designator", "Char Designator", "Note", "Note No", "Note Ref", "Char. Designator", "Bubble", "Bubble No" |
   | Requirement | "Requirement", "Description", "Characteristic", "Specification", "Requirement Description", "Char Description" |
   | Results | "Results", "Result", "Inspection Result", "Test Result", "Actual", "Measured Value", "Finding" |
   | Designed_Qualified_Tooling | "Designed / Qualified Tooling", "Tooling", "Qualified Tooling", "Design Tooling", "Tool Ref" |
   | Nonconformance_Number | "Nonconformance Number", "NCR", "NCR Number", "Nonconformance No", "Discrepancy No", "NC No" |

4. **REQUIREMENT FIELD EXTRACTION**
   - Extract the FULL requirement text exactly as printed
   - If the requirement spans multiple lines, join with a single space
   - Include any special symbols exactly as printed (e.g., "♦", "±", "°")
   - DO NOT truncate or summarize the requirement text

5. **CHARACTERISTIC DESIGNATOR EXTRACTION**
   - This field may contain values like: "Note 1", "Note 2 (Bubble #1)", "Note 5 (Bubble #1)"
   - Extract the FULL value including any bubble number reference in parentheses
   - Example: "Note 5 (Bubble #1)" → extract as "Note 5 (Bubble #1)"
   - DO NOT split into separate fields

6. **REFERENCE LOCATION EXTRACTION**
   - This field may contain multi-line values such as:
     "DWG# 4007843 Rev E. Sheet# 1"
     "DWG# 4007843 Rev E. BONDING Sheet# 3"
   - Join all lines with a single space
   - Example: "DWG# 4007843 Rev E. BONDING Sheet# 3"
   - If the value is "N/A" or blank → return "NOT PRESENT"

7. **RESULTS FIELD VALUES**
   - Return the result value EXACTLY as printed
   - Common values: "Pass", "Fail", "N/A", "OK", "NOK", numeric measurements
   - If blank or "N/A" → return "NOT PRESENT"

8. **DESIGNED / QUALIFIED TOOLING FIELD**
   - Return the value EXACTLY as printed
   - If blank or "N/A" → return "NOT PRESENT"

9. **NONCONFORMANCE NUMBER FIELD**
   - Return the value EXACTLY as printed
   - If blank or "N/A" → return "NOT PRESENT"

10. **REDACTED / MASKED VALUES**
    - If a value is visibly masked as "XXXXXXX", "XXXXX", "***", "####", "[REDACTED]", or similar → return it exactly as shown.
    - DO NOT attempt to infer or guess redacted values
    - APPLY THIS RULE TO BOTH header fields AND table rows

11. **MISSING VALUES**
    - If a field is blank, "N/A", "—", "-", "TBD", or not present in the document → return "NOT PRESENT"
    - DO NOT return empty string ""

12. **DATE FORMAT**
    - Return dates EXACTLY as printed
    - DO NOT reformat, convert, or standardize dates


13. **ROW COMPLETENESS**
    - Extract EVERY visible row from the characteristic table
    - DO NOT skip rows where some fields are blank
    - DO NOT skip rows marked as "DELETED" — extract them with Requirement = "DELETED"
    - Each row = one object in the "Characteristic_Table" array

14. **LANGUAGE VARIANTS**
    - The form may be in English, French, German, Spanish, or other languages
    - Match fields by SEMANTIC MEANING regardless of language
    - Return extracted VALUES exactly as printed (do not translate values)

15. **FORM STRUCTURE VARIANTS**
    - Some clients may merge fields, split fields, or add extra columns
    - Extract only what maps to the standard fields listed above
    - DO NOT create new fields not listed in the output format
    - If a client has extra fields with no standard equivalent → ignore them

### REQUIREMENT NOMINAL / TOLERANCE / LIMIT EXTRACTION

FROM THE **Requirement** FIELD, EXTRACT:
* Nominal
* Tol_1
* Tol_2
* Upper_Limit
* Lower_Limit

THE REQUIREMENT MAY LOOK LIKE:

* "0.120 +/-0.005” TYP"
* "0.080 +/-0.003” - 3 Places"
* "18.55 +/-0.25”"
* "90.0° +/-2.0°"

---

### EXTRACTION RULES
1. **Nominal**
   - Extract the main numeric value before the tolerance.
   - Example: `"0.120 +/-0.005” TYP"` → `"0.120"`

2. **Tol_1**
   - Extract the plus tolerance.
   - Example: `"0.120 +/-0.005” TYP"` → `"+0.005"`

3. **Tol_2**
   - Extract the minus tolerance.
   - Example: `"0.120 +/-0.005” TYP"` → `"-0.005"`

4. **Upper_Limit**
   - Calculate using:
     Nominal + Tol_1
   - Example:
     `"0.120 +/-0.005” TYP"`
     Upper_Limit = 0.120 + 0.005 = `"0.125"`

5. **Lower_Limit**
   - Calculate using:
     Nominal - absolute Tol_2
   - Example:
     `"0.120 +/-0.005” TYP"`
     Lower_Limit = 0.120 - 0.005 = `"0.115"`

EXAMPLES:
Requirement: `"0.120 +/-0.005” TYP"`
"Nominal": "0.120",
"Tol_1": "+0.005",
"Tol_2": "-0.005",
"Upper_Limit": "0.125",
"Lower_Limit": "0.115"

---
### STRICT NON-INTERPRETATION RULES
* NEVER CALCULATE, ESTIMATE, OR INFER DATA
* NEVER ADD OR REMOVE FIELDS FROM THE OUTPUT FORMAT
* DO NOT PROPAGATE CONTEXT TO FILL EMPTY CELLS
* DO NOT TRANSLATE OR CONVERT VALUES
* RETURN ONLY WHAT IS VISIBLY PRESENT IN THE DOCUMENT
* IF UNCERTAIN BETWEEN TWO FIELD MATCHES → PICK THE CLOSEST SEMANTIC MATCH AND EXTRACT AS-IS

"""

