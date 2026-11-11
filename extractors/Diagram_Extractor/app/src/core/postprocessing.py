import json
from loguru import logger as logging
import re

def parse_tolerance_table(header_data):
    """
    Extract tolerance rules from header data.
    Returns a dict mapping precision levels to tolerance values.
    Example: {'whole': 0.3, 'one_decimal': 0.1, 'two_decimal': 0.01, 'three_decimal': 0.005, 'angular': 2.0}
    """
    tolerance_rules = {
        'whole': None,
        'one_decimal': None,
        'two_decimal': None,
        'three_decimal': None,
        'angular': None,
        'linear': None,
        'diametral': None,
        'ranges': []
    }

    if not header_data:
        return tolerance_rules

    # First, try to extract from structured "Tolerance" field if present
    if isinstance(header_data, dict) and "Tolerance" in header_data:
        tolerance_section = header_data["Tolerance"]
        if isinstance(tolerance_section, dict):
            # Map the structured tolerance fields to our internal format
            mapping = {
                'whole': 'Whole Number',
                'one_decimal': 'One Place Decimal',
                'two_decimal': 'Two Place Decimal',
                'three_decimal': 'Three Place Decimal',
                'angular': 'Angular',
                'linear': 'Linear',
                'diametral': 'Diametral'
            }

            for key, field_name in mapping.items():
                value = tolerance_section.get(field_name, "Not Found")
                if value and value != "Not Found":
                    try:
                        # Extract numeric value from various formats:
                        # "+/- .1" → 0.1
                        # ".X ± .1" → 0.1
                        # "± 2" → 2.0
                        # Remove +/- symbols, ± symbol, degree symbol, spaces and placeholder text
                        clean_value = re.sub(r'[^\d.]', '', str(value))
                        if clean_value:
                            parsed_tolerance = float(clean_value)
                            tolerance_rules[key] = parsed_tolerance
                            logging.debug(f"Parsed tolerance {key}: {tolerance_rules[key]} from '{value}'")
                        else:
                            logging.debug(f"No numeric value found in {key}: '{value}'")
                    except (ValueError, TypeError) as e:
                        logging.warning(f"Could not parse tolerance value for {key}: {value} - {e}")

            # Parse range-based tolerances
            ranges = tolerance_section.get("Ranges", [])
            if isinstance(ranges, list) and ranges:
                for r in ranges:
                    try:
                        rmin = float(re.sub(r'[^\d.]', '', str(r.get("min", ""))))
                        rmax = float(re.sub(r'[^\d.]', '', str(r.get("max", ""))))
                        rtol = float(re.sub(r'[^\d.]', '', str(r.get("tolerance", ""))))
                        tolerance_rules['ranges'].append({'min': rmin, 'max': rmax, 'tolerance': rtol})
                        logging.debug(f"Parsed range tolerance: {rmin}-{rmax} = ±{rtol}")
                    except (ValueError, TypeError) as e:
                        logging.warning(f"Could not parse range tolerance: {r} - {e}")

            # If we found at least one tolerance value, return
            if any(v is not None for k, v in tolerance_rules.items() if k != 'ranges') or tolerance_rules.get('ranges'):
                logging.info(f"Successfully extracted tolerances from structured format: {tolerance_rules}")
                return tolerance_rules

    # Fallback: Search for tolerance information using regex patterns
    header_text = json.dumps(header_data).upper()

    patterns = {
        'whole': r'WHOLE\s*NUMBER[:\s]*[±+\-]*\s*([0-9.]+)',
        'one_decimal': r'ONE\s*PLACE\s*DECIMAL[:\s]*[±+\-]*\s*([0-9.]+)',
        'two_decimal': r'TWO\s*PLACE\s*DECIMAL[:\s]*[±+\-]*\s*([0-9.]+)',
        'three_decimal': r'THREE\s*PLACE\s*DECIMAL[:\s]*[±+\-]*\s*([0-9.]+)',
        'angular': r'ANGULAR[:\s]*[±+\-]*\s*([0-9.]+)[°]*'
    }

    for key, pattern in patterns.items():
        match = re.search(pattern, header_text, re.IGNORECASE)
        if match:
            try:
                tolerance_rules[key] = abs(float(match.group(1)))  # Ensure positive
                logging.debug(f"Found tolerance rule via regex: {key} = ±{tolerance_rules[key]}")
            except ValueError:
                logging.warning(f"Could not parse tolerance value for {key}")

    logging.info(f"Final tolerance rules: {tolerance_rules}")
    return tolerance_rules


def parse_governing_standard(header_data):
    """
    Extract the governing drafting standard (ASME Y14.5 vs ISO) from header data.
    Returns a dict with standard info and implications:
    - standard: 'ASME' | 'ISO' | None
    - version: e.g. '2018', '1101', None
    - full_designation: e.g. 'ASME Y14.5-2018'
    - envelope_rule: True (ASME Rule #1) | False (ISO Independency Principle)
    """
    result = {
        'standard': None,
        'version': None,
        'full_designation': None,
        'envelope_rule': False
    }

    if not header_data:
        return result

    # Get governing standard from structured field first
    if isinstance(header_data, dict):
        gov_std = header_data.get('Governing_Standard', '')
        if gov_std and gov_std != 'Not Found':
            header_text = gov_std.upper()
        else:
            header_text = json.dumps(header_data).upper()
    else:
        header_text = str(header_data).upper()

    # ASME patterns
    asme_patterns = [
        r'ASME\s*Y14\.5[M]?\s*[-–]?\s*(20\d{2}|\d{4})?',
        r'ANSI\s*Y14\.5[M]?\s*[-–]?\s*(19\d{2}|\d{4})?',
        r'PER\s+ASME\s*Y14',
    ]

    for pattern in asme_patterns:
        match = re.search(pattern, header_text, re.IGNORECASE)
        if match:
            result['standard'] = 'ASME'
            result['envelope_rule'] = True  # ASME Rule #1: Envelope Principle
            if match.lastindex and match.group(1):
                result['version'] = match.group(1)
                result['full_designation'] = f"ASME Y14.5-{match.group(1)}"
            else:
                result['full_designation'] = 'ASME Y14.5'
            logging.info(f"Governing standard detected: {result['full_designation']} (Envelope Principle applies)")
            return result

    # ISO patterns
    iso_patterns = [
        r'ISO\s*8015\s*[-–]?\s*(20\d{2})?',
        r'ISO\s*1101\s*[-–]?\s*(20\d{2})?',
        r'EN\s*ISO\s*1101',
        r'ISO\s*GPS',
        r'BS\s*8888',
    ]

    for pattern in iso_patterns:
        match = re.search(pattern, header_text, re.IGNORECASE)
        if match:
            result['standard'] = 'ISO'
            result['envelope_rule'] = False  # ISO Independency Principle
            if match.lastindex and match.group(1):
                result['version'] = match.group(1)
            result['full_designation'] = match.group(0).strip()
            logging.info(f"Governing standard detected: {result['full_designation']} (Independency Principle applies)")
            return result

    logging.info("No governing standard detected in header data")
    return result


def parse_iso_fit_code(nominal_str):
    """
    Recognize ISO 286 fit codes in a nominal string and return structured info.
    Examples: 'H7/g6', '50H7/g6', 'Ø25 H7', '30 js6'

    Returns None if no fit code is detected.
    Returns a dict with:
    - nominal_diameter: float or None
    - hole_code: e.g. 'H7' or None
    - shaft_code: e.g. 'g6' or None
    - fit_type: 'Clearance' | 'Transition' | 'Interference' | 'Unknown'
    """
    if not nominal_str or not isinstance(nominal_str, str):
        return None

    clean = nominal_str.replace('Ø', '').replace('⌀', '').strip()

    # Pattern: optional diameter + hole/shaft codes
    # Matches: "50 H7/g6", "H7/g6", "50H7", "25 js6", "H7"
    fit_pattern = r'(\d+(?:\.\d+)?)?\s*([A-Z][a-z]?\d{1,2})\s*/\s*([a-z][a-z]?\d{1,2})'
    match = re.search(fit_pattern, clean)

    if match:
        diameter = float(match.group(1)) if match.group(1) else None
        hole_code = match.group(2)
        shaft_code = match.group(3)

        # Determine fit type based on fundamental deviation letters
        fit_type = _determine_fit_type(hole_code, shaft_code)

        result = {
            'nominal_diameter': diameter,
            'hole_code': hole_code,
            'shaft_code': shaft_code,
            'fit_type': fit_type
        }
        logging.debug(f"Parsed ISO fit code from '{nominal_str}': {result}")
        return result

    # Single code pattern (hole or shaft only): "50 H7", "25 js6"
    single_pattern = r'(\d+(?:\.\d+)?)\s*([A-Za-z][a-z]?\d{1,2})$'
    match = re.search(single_pattern, clean)
    if match:
        diameter = float(match.group(1))
        code = match.group(2)
        is_hole = code[0].isupper()

        result = {
            'nominal_diameter': diameter,
            'hole_code': code if is_hole else None,
            'shaft_code': code if not is_hole else None,
            'fit_type': 'Unknown'
        }
        logging.debug(f"Parsed single ISO fit code from '{nominal_str}': {result}")
        return result

    return None


def _determine_fit_type(hole_code, shaft_code):
    """
    Determine the fit type (Clearance/Transition/Interference) from ISO 286 codes.
    Based on fundamental deviation letters.
    """
    if not hole_code or not shaft_code:
        return 'Unknown'

    # Extract the fundamental deviation letter from shaft code
    shaft_letter = shaft_code[0]

    # Clearance: shaft deviations a-h (always smaller than H hole)
    if shaft_letter in 'abcdefgh':
        return 'Clearance'
    # Transition: shaft deviations j-n (overlap with H hole)
    elif shaft_letter in 'jklmn':
        return 'Transition'
    # Interference: shaft deviations p-z (always larger than H hole)
    elif shaft_letter in 'prstuvxyz':
        return 'Interference'
    else:
        return 'Unknown'


def classify_gdt_symbol(nominal_str):
    """
    Map GD&T text descriptions to their category and datum requirements.
    Returns None if no GD&T symbol is recognized.
    Returns a dict with:
    - symbol: canonical name
    - category: 'Form' | 'Profile' | 'Orientation' | 'Location' | 'Runout'
    - datum_required: bool
    - obsolete: bool (True for Concentricity and Symmetry per ASME Y14.5-2018)
    """
    if not nominal_str or not isinstance(nominal_str, str):
        return None

    text = nominal_str.upper().strip()

    # GD&T symbol classification table per ASME Y14.5-2018
    gdt_table = {
        # Form tolerances - no datums
        'STRAIGHTNESS': {'category': 'Form', 'datum_required': False, 'obsolete': False},
        'FLATNESS': {'category': 'Form', 'datum_required': False, 'obsolete': False},
        'CIRCULARITY': {'category': 'Form', 'datum_required': False, 'obsolete': False},
        'ROUNDNESS': {'category': 'Form', 'datum_required': False, 'obsolete': False},
        'CYLINDRICITY': {'category': 'Form', 'datum_required': False, 'obsolete': False},
        # Profile tolerances - datums optional but common
        'PROFILE OF A LINE': {'category': 'Profile', 'datum_required': False, 'obsolete': False},
        'PROFILE OF A SURFACE': {'category': 'Profile', 'datum_required': False, 'obsolete': False},
        'LINE PROFILE': {'category': 'Profile', 'datum_required': False, 'obsolete': False},
        'SURFACE PROFILE': {'category': 'Profile', 'datum_required': False, 'obsolete': False},
        # Orientation tolerances - datums always required
        'PERPENDICULARITY': {'category': 'Orientation', 'datum_required': True, 'obsolete': False},
        'PARALLELISM': {'category': 'Orientation', 'datum_required': True, 'obsolete': False},
        'ANGULARITY': {'category': 'Orientation', 'datum_required': True, 'obsolete': False},
        # Location tolerances - datums always required
        'POSITION': {'category': 'Location', 'datum_required': True, 'obsolete': False},
        'TRUE POSITION': {'category': 'Location', 'datum_required': True, 'obsolete': False},
        # Runout tolerances - datums always required
        'CIRCULAR RUNOUT': {'category': 'Runout', 'datum_required': True, 'obsolete': False},
        'TOTAL RUNOUT': {'category': 'Runout', 'datum_required': True, 'obsolete': False},
        'RUNOUT': {'category': 'Runout', 'datum_required': True, 'obsolete': False},
        # Obsolete per ASME Y14.5-2018
        'CONCENTRICITY': {'category': 'Location', 'datum_required': True, 'obsolete': True},
        'SYMMETRY': {'category': 'Location', 'datum_required': True, 'obsolete': True},
    }

    # Try to match the beginning of the text against known GD&T symbols
    for symbol_name, info in gdt_table.items():
        if text.startswith(symbol_name):
            result = {
                'symbol': symbol_name.title(),
                'category': info['category'],
                'datum_required': info['datum_required'],
                'obsolete': info['obsolete']
            }
            if info['obsolete']:
                logging.warning(f"Obsolete GD&T symbol detected: {symbol_name} (removed in ASME Y14.5-2018)")
            logging.debug(f"Classified GD&T symbol '{nominal_str}': {result}")
            return result

    return None


def normalize_thread_callout(nominal_str):
    """
    Parse and normalize thread callout strings into structured metadata.
    Supports both Metric (ISO 68-1) and Unified National (ASME/ANSI) formats.

    Returns None if no thread callout is detected.
    Returns a dict with:
    - format: 'Metric' | 'Imperial'
    - nominal_diameter: str (e.g. '10', '1/4', '#10')
    - pitch_or_tpi: str (e.g. '1.5', '20')
    - series: str (e.g. 'UNC', 'UNF', 'UNEF') or None
    - tolerance_class: str (e.g. '6H', '2B') or None
    - thread_type: 'Internal' | 'External' | None
    - left_hand: bool
    - normalized: str (the cleaned canonical string)
    """
    if not nominal_str or not isinstance(nominal_str, str):
        return None

    text = nominal_str.strip()

    # Check for left-hand thread
    left_hand = bool(re.search(r'\bLH\b', text, re.IGNORECASE))

    # Metric thread pattern: M[diameter] x [pitch]-[tolerance class]
    # Examples: M10 x 1.5-6H, M8x1.25-6g, M10, M16 x 1.5-6H LH
    metric_pattern = r'^M\s*(\d+(?:\.\d+)?)\s*(?:[xX×]\s*(\d+(?:\.\d+)?))?\s*(?:[-–]\s*(\d+[A-Za-z]+[a-z]?))?\s*'
    metric_match = re.match(metric_pattern, text, re.IGNORECASE)

    if metric_match and text.upper().startswith('M'):
        diameter = metric_match.group(1)
        pitch = metric_match.group(2)
        tol_class = metric_match.group(3)

        # Determine internal vs external from tolerance class
        thread_type = None
        if tol_class:
            # Capital letter = internal (H, G), lowercase = external (g, h, f)
            tol_letters = re.findall(r'[A-Za-z]', tol_class)
            if tol_letters:
                thread_type = 'Internal' if tol_letters[-1].isupper() else 'External'

        result = {
            'format': 'Metric',
            'nominal_diameter': diameter,
            'pitch_or_tpi': pitch,
            'series': None,
            'tolerance_class': tol_class,
            'thread_type': thread_type,
            'left_hand': left_hand,
            'normalized': text.strip()
        }
        logging.debug(f"Parsed metric thread callout '{nominal_str}': {result}")
        return result

    # Imperial thread pattern: [diameter]-[TPI] [series]-[class]
    # Examples: 1/4-20 UNC-2B, #10-32 UNF-2A, 3/8-16 UNC-2A LH
    imperial_pattern = r'^([#]?\d+(?:/\d+)?)\s*[-–]\s*(\d+)\s*(UN[CEFJSN]?[EF]?)\s*(?:[-–]\s*(\d[AB]))?\s*'
    imperial_match = re.match(imperial_pattern, text, re.IGNORECASE)

    if imperial_match:
        diameter = imperial_match.group(1)
        tpi = imperial_match.group(2)
        series = imperial_match.group(3)
        fit_class = imperial_match.group(4)

        thread_type = None
        if fit_class:
            thread_type = 'Internal' if fit_class.upper().endswith('B') else 'External'

        result = {
            'format': 'Imperial',
            'nominal_diameter': diameter,
            'pitch_or_tpi': tpi,
            'series': series.upper() if series else None,
            'tolerance_class': fit_class,
            'thread_type': thread_type,
            'left_hand': left_hand,
            'normalized': text.strip()
        }
        logging.debug(f"Parsed imperial thread callout '{nominal_str}': {result}")
        return result

    return None


def normalize_surface_finish(nominal_str):
    """
    Parse surface finish callouts into structured metadata.
    Examples: 'Ra 1.6', 'Rz 12.5', 'Ra 0.8 P', 'Rz 6.3 M'

    Returns None if no surface finish callout is detected.
    Returns a dict with:
    - parameter: 'Ra' | 'Rz' | 'Rq' | 'Rt' | other
    - value: float
    - unit: 'µm' (default for surface roughness)
    - lay: str or None (=, ⊥, X, M, C, R, P)
    - removal_required: True | False | None
    """
    if not nominal_str or not isinstance(nominal_str, str):
        return None

    text = nominal_str.strip()

    # Surface roughness parameter pattern
    # Matches: Ra 1.6, Rz 12.5, Rq 0.4, Ra1.6, etc.
    roughness_pattern = r'(R[azqtp])\s*(\d+(?:\.\d+)?)'
    match = re.search(roughness_pattern, text, re.IGNORECASE)

    if match:
        parameter = match.group(1)
        # Normalize parameter capitalization (Ra, Rz, Rq, Rt, Rp)
        parameter = 'R' + parameter[1].lower()
        value = float(match.group(2))

        # Check for lay symbol after the value
        lay = None
        lay_pattern = r'(?:R[azqtp]\s*\d+(?:\.\d+)?)\s+([=⊥XMCRP])\b'
        lay_match = re.search(lay_pattern, text, re.IGNORECASE)
        if lay_match:
            lay = lay_match.group(1).upper()

        result = {
            'parameter': parameter,
            'value': value,
            'unit': 'µm',
            'lay': lay,
            'removal_required': None  # Determined by checkmark modifier, not extractable from text alone
        }
        logging.debug(f"Parsed surface finish callout '{nominal_str}': {result}")
        return result

    return None


def enrich_measurement_metadata(measurement, governing_standard_info=None):
    """
    Enrich a measurement dict with optional metrological metadata.
    Adds new fields based on pattern recognition in Nominal and Characteristic_Type.
    Never overwrites existing fields — only adds new enrichment keys.

    New fields added (all optional, only present when detected):
    - Fit_Code_Info: dict from parse_iso_fit_code
    - Thread_Info: dict from normalize_thread_callout
    - Surface_Finish_Info: dict from normalize_surface_finish
    - GDT_Info: dict from classify_gdt_symbol
    - Governing_Standard: dict from governing_standard_info (passed through)
    """
    if not measurement:
        return measurement

    nominal = measurement.get('Nominal', '')
    char_type = measurement.get('Characteristic_Type', '')

    # ISO fit code detection (on diameter types or when fit code patterns are present)
    if nominal and isinstance(nominal, str):
        fit_info = parse_iso_fit_code(nominal)
        if fit_info:
            measurement['Fit_Code_Info'] = fit_info

    # Thread callout detection
    if nominal and isinstance(nominal, str):
        thread_info = normalize_thread_callout(nominal)
        if thread_info:
            measurement['Thread_Info'] = thread_info

    # Surface finish detection
    if char_type and 'surface finish' in char_type.lower():
        if nominal and isinstance(nominal, str):
            sf_info = normalize_surface_finish(nominal)
            if sf_info:
                measurement['Surface_Finish_Info'] = sf_info

    # GD&T classification
    if char_type and 'geometric tolerance' in char_type.lower():
        if nominal and isinstance(nominal, str):
            gdt_info = classify_gdt_symbol(nominal)
            if gdt_info:
                measurement['GDT_Info'] = gdt_info

    # Attach governing standard info if available
    if governing_standard_info and governing_standard_info.get('standard'):
        measurement['Governing_Standard'] = governing_standard_info

    return measurement


def get_decimal_places(value_str):
    """
    Determine the number of decimal places in a numeric string.
    Returns None if not a valid number.
    """
    if not value_str or value_str == "":
        return None

    try:
        # Remove any non-numeric characters except decimal point and minus
        clean_value = re.sub(r'[^\d.\-]', '', str(value_str))
        # Parse the value
        float_val = float(clean_value)

        # Check if the value is negative (invalid for measurements)
        if float_val < 0:
            logging.warning(f"Negative measurement value detected: {value_str}, taking absolute value")
            clean_value = str(abs(float_val))

        if '.' in clean_value:
            return len(clean_value.split('.')[-1])
        else:
            return 0  # Whole number
    except:
        return None


def determine_applicable_tolerance(nominal, tolerance_rules, characteristic_type):
    """
    Determine which tolerance rule applies based on nominal value precision or range.
    """
    if not nominal or nominal == "":
        return None

    # Check if it's an angular measurement
    if characteristic_type and ('angular' in characteristic_type.lower() or 'angle' in characteristic_type.lower()):
        return tolerance_rules.get('angular')

    # Check for diametral vs linear specific tolerances
    if characteristic_type:
        ctype_lower = characteristic_type.lower()
        if 'diameter' in ctype_lower or 'diametral' in ctype_lower or 'Ø' in characteristic_type:
            if tolerance_rules.get('diametral') is not None:
                return tolerance_rules.get('diametral')
        elif 'linear' in ctype_lower:
            if tolerance_rules.get('linear') is not None:
                return tolerance_rules.get('linear')

    try:
        nom_val = float(re.sub(r'[^\d.]', '', str(nominal)))
        for r in tolerance_rules.get('ranges', []):
            if r['min'] <= nom_val <= r['max']:
                return r['tolerance']
    except ValueError:
        pass

    decimal_places = get_decimal_places(nominal)

    tol_by_decimal = None
    if decimal_places == 0:
        tol_by_decimal = tolerance_rules.get('whole')
    elif decimal_places == 1:
        tol_by_decimal = tolerance_rules.get('one_decimal')
    elif decimal_places == 2:
        tol_by_decimal = tolerance_rules.get('two_decimal')
    elif decimal_places is not None and decimal_places >= 3:
        tol_by_decimal = tolerance_rules.get('three_decimal')

    if tol_by_decimal is not None:
        return tol_by_decimal

    # Fallback: if no decimal-place specific tolerance matched, and this wasn't
    # marked as diametral/angular explicitly, apply the general 'linear' tolerance if it exists
    if tolerance_rules.get('linear') is not None:
        if not characteristic_type or not any(x in characteristic_type.lower() for x in ['diameter', 'diametral', 'angle', 'angular', 'ø', 'radius']):
            return tolerance_rules.get('linear')

    return None


def sanitize_measurement_value(value_str, field_name="value"):
    """
    Convert a measurement string to a positive float.
    Raises ValueError if the value is invalid.
    """
    if not value_str or value_str == "":
        return None

    try:
        # Remove any non-numeric characters except decimal point and minus
        clean_value = re.sub(r'[^\d.\-]', '', str(value_str))
        float_val = float(clean_value)

        # Measurements cannot be negative
        if float_val < 0:
            logging.warning(f"Negative {field_name} detected: {value_str}, taking absolute value")
            float_val = abs(float_val)

        return float_val
    except (ValueError, TypeError):
        logging.error(f"Invalid {field_name}: {value_str}")
        return None


def apply_tolerance_to_measurement(measurement, tolerance_rules):
    """
    Apply tolerance rules to a single measurement and fill all fields.

    Special handling for Geometric Tolerance:
    - Upper/Lower Tolerance: empty
    - Upper Limit: same as Nominal
    - Lower Limit: empty

    Priority for other types:
    1. If Upper Limit/Lower Limit exist → infer Tolerances
    2. If Explicit Tolerances exist → calculate Upper Limit/Lower Limit
    3. If only Nominal exists → apply header tolerance → calculate all

    Note: All measurements (nominal, upper limit, lower limit) must be positive.
    Tolerances are stored with +/- signs but represent absolute deviations.
    """
    # Accept both underscore and space versions for input compatibility
    nominal = measurement.get('Nominal', '')
    tolerance_1 = measurement.get('Tolerance_1', '') or measurement.get('Tolerance 1', '')
    tolerance_2 = measurement.get('Tolerance_2', '') or measurement.get('Tolerance 2', '')
    upper_limit = measurement.get('Upper_Limit', '') or measurement.get('Upper Limit', '')
    lower_limit = measurement.get('Lower_Limit', '') or measurement.get('Lower Limit', '')
    characteristic_type = measurement.get('Characteristic_Type', '') or measurement.get('Characteristic Type', '')

    try:
        # Special Case: Geometric Tolerance
        if characteristic_type and 'geometric tolerance' in characteristic_type.lower():
            if nominal and nominal != "":
                nominal_val = sanitize_measurement_value(nominal, "Nominal")
                if nominal_val is None:
                    return measurement

                # Set Upper Limit = Nominal, Lower Limit = empty, Tolerances = empty
                measurement['Nominal'] = f"{nominal_val:.6f}".rstrip('0').rstrip('.')
                measurement['Tolerance_1'] = ""
                measurement['Tolerance_2'] = ""
                measurement['Upper Limit'] = f"{nominal_val:.6f}".rstrip('0').rstrip('.')
                measurement['Lower Limit'] = ""

                # Remove underscore versions if they exist
                measurement.pop('Upper_Limit', None)
                measurement.pop('Lower_Limit', None)
                measurement.pop('Tolerance 1', None)
                measurement.pop('Tolerance 2', None)

                logging.info(f"Applied Geometric Tolerance logic for Serial {measurement.get('Serial_number')}: Upper={nominal_val}, Lower=empty")
                return measurement
            else:
                logging.warning(f"Geometric Tolerance without nominal for Serial {measurement.get('Serial_number')}")
                return measurement

        # Special Case: REF dimensions — informational only, no tolerance, no limits
        # REF dimensions carry no tolerance and must never trigger false non-conformance.
        # This early-exit ensures that even if tolerances were accidentally extracted for
        # a REF dimension, they get cleared.
        dimension_type = measurement.get('Dimension_Type', '') or measurement.get('Dimension Type', '')
        if dimension_type and dimension_type.upper() == 'REF':
            if nominal and nominal != "":
                nominal_val = sanitize_measurement_value(nominal, "Nominal")
                if nominal_val is not None:
                    measurement['Nominal'] = f"{nominal_val:.6f}".rstrip('0').rstrip('.')
            measurement['Tolerance_1'] = ""
            measurement['Tolerance_2'] = ""
            measurement['Upper Limit'] = ""
            measurement['Lower Limit'] = ""
            # Remove underscore versions if they exist
            measurement.pop('Upper_Limit', None)
            measurement.pop('Lower_Limit', None)
            measurement.pop('Tolerance 1', None)
            measurement.pop('Tolerance 2', None)
            logging.info(f"Applied REF dimension logic for Serial {measurement.get('Serial_number')}: no tolerance, no limits")
            return measurement

        # Case 1: Upper and Lower Limits are provided
        if upper_limit and lower_limit and upper_limit != "" and lower_limit != "":
            upper_val = sanitize_measurement_value(upper_limit, "Upper Limit")
            lower_val = sanitize_measurement_value(lower_limit, "Lower Limit")

            if upper_val is None or lower_val is None:
                logging.error(f"Invalid upper/lower limits for Serial {measurement.get('Serial_number')}")
                return measurement

            # Validate that upper > lower
            if upper_val <= lower_val:
                logging.error(f"Invalid limits for Serial {measurement.get('Serial_number')}: Upper ({upper_val}) <= Lower ({lower_val})")
                return measurement

            # Calculate or validate nominal
            if nominal and nominal != "":
                nominal_val = sanitize_measurement_value(nominal, "Nominal")
                if nominal_val is None:
                    nominal_val = (upper_val + lower_val) / 2
                    logging.warning(f"Invalid nominal for Serial {measurement.get('Serial_number')}, calculated from limits: {nominal_val}")
            else:
                nominal_val = (upper_val + lower_val) / 2
                logging.info(f"Nominal not provided for Serial {measurement.get('Serial_number')}, calculated: {nominal_val}")

            # Validate nominal is between limits
            if not (lower_val <= nominal_val <= upper_val):
                logging.warning(f"Nominal {nominal_val} outside limits [{lower_val}, {upper_val}] for Serial {measurement.get('Serial_number')}")
                nominal_val = (upper_val + lower_val) / 2
                logging.info(f"Recalculated nominal to midpoint: {nominal_val}")

            # Infer tolerances
            tol_1 = upper_val - nominal_val
            tol_2 = nominal_val - lower_val

            measurement['Nominal'] = f"{nominal_val:.6f}".rstrip('0').rstrip('.')
            measurement['Tolerance_1'] = f"+{tol_1:.6f}".rstrip('0').rstrip('.')
            measurement['Tolerance_2'] = f"-{tol_2:.6f}".rstrip('0').rstrip('.')
            measurement['Upper Limit'] = f"{upper_val:.6f}".rstrip('0').rstrip('.')
            measurement['Lower Limit'] = f"{lower_val:.6f}".rstrip('0').rstrip('.')

            # Remove underscore versions if they exist
            measurement.pop('Upper_Limit', None)
            measurement.pop('Lower_Limit', None)

            logging.debug(f"Applied Case 1 (Limits→Tolerances) for Serial {measurement.get('Serial_number')}")
            return measurement

        # Case 2: Explicit tolerances are provided
        if tolerance_1 or tolerance_2 and tolerance_1 != "" or tolerance_2 != "":
            if not nominal or nominal == "":
                logging.warning(f"Tolerance without nominal for Serial {measurement.get('Serial_number')}")
                return measurement

            nominal_val = sanitize_measurement_value(nominal, "Nominal")
            if nominal_val is None:
                return measurement


            has_upper_tolerance = bool(tolerance_1)
            has_lower_tolerance = bool(tolerance_2)
            try:
                tol_1 = abs(float(str(tolerance_1).replace('+', '').replace('-', ''))) if has_upper_tolerance else None
                tol_2 = abs(float(str(tolerance_2).replace('+', '').replace('-', ''))) if has_lower_tolerance else None
            except ValueError:
                logging.error(f"Invalid tolerance values for Serial {measurement.get('Serial_number')}")
                return measurement

            upper_val = nominal_val + tol_1 if tol_1 is not None else None
            lower_val = nominal_val - tol_2 if tol_2 is not None else None

            if lower_val is not None and lower_val < 0:
                logging.error(f"Calculated lower limit is negative ({lower_val}) for Serial {measurement.get('Serial_number')}")
                logging.info(f"This may indicate an error in nominal ({nominal_val}) or tolerance ({tol_2})")
                return measurement

            measurement['Upper Limit'] = (
                f"{upper_val:.6f}".rstrip('0').rstrip('.') if upper_val is not None else ""
            )
            measurement['Lower Limit'] = (
                f"{lower_val:.6f}".rstrip('0').rstrip('.') if lower_val is not None else ""
            )

            # Remove underscore versions if they exist
            measurement.pop('Upper_Limit', None)
            measurement.pop('Lower_Limit', None)

            logging.debug(f"Applied Case 2 (Tolerances→Limits) for Serial {measurement.get('Serial_number')}")
            return measurement

        # Case 3: Only nominal is provided - apply header tolerance
        if nominal and nominal != "" and (not tolerance_1 or tolerance_1 == ""):
            nominal_val = sanitize_measurement_value(nominal, "Nominal")
            measurement['Dimension_Type'] = measurement.get('Dimension_Type', '') or measurement.get('Dimension Type', '')
            if not measurement['Dimension_Type'] or measurement['Dimension_Type'].lower() == 'basic':
                measurement['Dimension_Type'] = 'Basic'
            if nominal_val is None:
                return measurement

            # Do not apply general header tolerances to MAX, MIN, or REF dimensions
            if measurement['Dimension_Type'].upper() in ['MAX', 'MIN', 'REF']:
                return measurement

            applicable_tolerance = determine_applicable_tolerance(nominal, tolerance_rules, characteristic_type)

            if applicable_tolerance is not None:
                # Calculate limits
                upper_val = nominal_val + applicable_tolerance
                lower_val = nominal_val - applicable_tolerance

                # Validate lower limit is not negative
                if lower_val < 0:
                    logging.warning(f"Header tolerance would result in negative lower limit for Serial {measurement.get('Serial_number')}")
                    logging.info(f"Nominal: {nominal_val}, Tolerance: ±{applicable_tolerance}")
                    # Adjust tolerance to prevent negative lower limit
                    adjusted_tolerance = nominal_val * 0.9  # Use 90% of nominal as max tolerance
                    logging.info(f"Adjusting tolerance to ±{adjusted_tolerance} to keep lower limit positive")
                    applicable_tolerance = adjusted_tolerance
                    lower_val = nominal_val - applicable_tolerance
                    upper_val = nominal_val + applicable_tolerance

                # Apply symmetric tolerance
                measurement['Tolerance_1'] = f"+{applicable_tolerance:.6f}".rstrip('0').rstrip('.')
                measurement['Tolerance_2'] = f"-{applicable_tolerance:.6f}".rstrip('0').rstrip('.')
                measurement['Upper Limit'] = f"{upper_val:.6f}".rstrip('0').rstrip('.')
                measurement['Lower Limit'] = f"{lower_val:.6f}".rstrip('0').rstrip('.')


                # Remove underscore versions if they exist
                measurement.pop('Upper_Limit', None)
                measurement.pop('Lower_Limit', None)

                logging.info(f"Applied Case 3 (Header Tolerance) for Serial {measurement.get('Serial_number')}: ±{applicable_tolerance}")
                return measurement
            else:
                logging.warning(f"No applicable tolerance found for Serial {measurement.get('Serial_number')} with nominal {nominal}")

    except (ValueError, TypeError) as e:
        logging.error(f"Error applying tolerance to Serial {measurement.get('Serial_number')}: {e}")

    return measurement

def process_table(extracted_table_list):
    combined_table = []
    if not extracted_table_list:
        return combined_table
    for data in extracted_table_list:
        if data:
            try:
                table_json = json.loads(data)
                combined_table.append(table_json)
            except json.JSONDecodeError as e:
                logging.error(f"Error decoding Table JSON data: {e}")
    return combined_table


def process_header(extracted_header_list):
    """
    Process header data extracted from class-0 YOLO crops.
    Handles single dicts, lists of dicts (from LLM responses),
    and merges multiple header extractions if present.
    Always returns a dictionary (never None, never a list).
    """
    if not extracted_header_list:
        return {}

    merged_header = {}

    def _merge_dict(src: dict, dst: dict):
        for k, v in src.items():
            if k == "Tolerance" and isinstance(v, dict):
                if "Tolerance" not in dst or not isinstance(dst.get("Tolerance"), dict):
                    dst["Tolerance"] = {}
                dst_tol = dst["Tolerance"]
                for tol_k, tol_v in v.items():
                    if tol_k == "Ranges" and isinstance(tol_v, list) and tol_v:
                        if not dst_tol.get("Ranges"):
                            dst_tol["Ranges"] = tol_v
                    elif _is_missing(dst_tol.get(tol_k)) and not _is_missing(tol_v):
                        dst_tol[tol_k] = tol_v
            elif k == "Title_Block_Bubbles" and isinstance(v, list):
                if "Title_Block_Bubbles" not in dst or not isinstance(dst.get("Title_Block_Bubbles"), list):
                    dst["Title_Block_Bubbles"] = []
                existing_serials = {
                    str(b.get("Serial_number", "")).strip()
                    for b in dst["Title_Block_Bubbles"]
                    if isinstance(b, dict)
                }
                for b in v:
                    if isinstance(b, dict):
                        s = str(b.get("Serial_number", "")).strip()
                        if s and s not in existing_serials:
                            dst["Title_Block_Bubbles"].append(b)
                            existing_serials.add(s)
            else:
                if k not in dst or _is_missing(dst.get(k)):
                    if not _is_missing(v):
                        dst[k] = v
                    elif k not in dst:
                        dst[k] = v
                elif dst.get(k) is None:
                    dst[k] = v

    for item in extracted_header_list:
        if not item:
            continue
        try:
            parsed = json.loads(item) if isinstance(item, str) else item
            if isinstance(parsed, list):
                for sub_item in parsed:
                    if isinstance(sub_item, dict):
                        _merge_dict(sub_item, merged_header)
            elif isinstance(parsed, dict):
                _merge_dict(parsed, merged_header)
        except json.JSONDecodeError as e:
            logging.error(f"Error parsing header JSON: {e}")

    return merged_header


def merge_text_into_measurements(measurements, text_details):
    """
    Merge text details directly into measurements list.
    Creates measurement entries for text with:
    - Serial_number: from text data (or "Not found" if no serial)
    - Characteristic_Type: "Text"
    - Nominal: the extracted text
    - All other fields: empty strings

    Args:
        measurements: List of measurement dictionaries
        text_details: List of dictionaries with format [{"serial_no": "key", "text": "value"}, ...]

    Returns:
        Combined list with measurements and text merged together
    """
    if not text_details:
        return measurements

    merged_list = measurements.copy() if measurements else []

    for text_item in text_details:
        serial_no = text_item.get("serial_no", "")
        text_content = text_item.get("text", "")
        dr_value = text_item.get("dr_no", "")
        is_this_note_number = text_item.get("Is_this_Note_Number")

        special_char_symbol = dr_value if dr_value else ""

        # Verify datum references (apply same postprocessing as bubble data dr_no)
        if dr_value:
            if  dr_value != "1":
                logging.debug(f"Non-standard dr_no detected in text: {dr_value} in processing")
                dr_value = "1"
                text_item["dr_no"] = "1"  # Update it in the item as well
                logging.debug(f"Converted non-standard text dr_no to 1")

        # Skip if no text content or default empty message
        if not text_content or text_content in ["No text data extracted", "No valid text data could be extracted"]:
            continue

        # Normalize the note-number flag (may arrive as a bool or a string like "true")
        is_note_number = (
            str(is_this_note_number).strip().lower() in ("true", "yes", "1")
            if is_this_note_number is not None else False
        )

        # If the extracted number is truly a NOTE number (not a bubble/serial number),
        # it must NOT be used as Serial_number. Store it in "Note Number" instead and
        # never merge it into an existing measurement with the same serial.
        if is_note_number and serial_no and serial_no != "Not Found":
            text_measurement = {
                "Serial_number": "",
                "Characteristic_Type": "Notes",
                "Nominal": text_content,
                "Tolerance_1": "",
                "Tolerance_2": "",
                "Upper Limit": "",
                "Lower Limit": "",
                "Dimension_Type": "",
                "Repeat": "",
                "dr_no": dr_value,
                "Special Character Symbol": special_char_symbol,
                "Note Number": serial_no,
                "Is_this_Note_Number": is_this_note_number
            }
            merged_list.append(text_measurement)
            logging.info(f"Added text measurement with Note Number {serial_no} (not treated as serial)")
            continue

        # Handle "Not Found" serial numbers - create entry with "Not found" as serial
        if serial_no == "Not Found":
            text_measurement = {
                "Serial_number": "",
                "Characteristic_Type": "Notes",
                "Nominal": text_content,
                "Tolerance_1": "",
                "Tolerance_2": "",
                "Upper Limit": "",
                "Lower Limit": "",
                "Dimension_Type": "",
                "Repeat": "",
                "dr_no": dr_value,
                "Special Character Symbol": special_char_symbol,
                "Note Number": ""
            }
            if is_this_note_number is not None:
                text_measurement["Is_this_Note_Number"] = is_this_note_number
            merged_list.append(text_measurement)
            logging.info(f"Added text measurement with 'Not found' serial number")
            continue

        # Check if this serial number already exists in measurements
        existing_measurement = next(
            (m for m in merged_list if m.get("Serial_number") == serial_no),
            None
        )

        if existing_measurement:
            # Serial exists - check if it's empty or should be updated
            if not existing_measurement.get("Characteristic_Type") or existing_measurement.get("Characteristic_Type") == "":
                # Empty measurement, fill with text
                existing_measurement["Characteristic_Type"] = "Notes"
                existing_measurement["Nominal"] = text_content
                if dr_value:
                    existing_measurement["dr_no"] = dr_value
                existing_measurement["Special Character Symbol"] = special_char_symbol
                existing_measurement.setdefault("Note Number", "")
                if is_this_note_number is not None:
                    existing_measurement["Is_this_Note_Number"] = is_this_note_number
                logging.info(f"Updated empty Serial {serial_no} with text content")
            else:
                # Measurement exists with data, create separate text entry
                text_measurement = {
                    "Serial_number": serial_no,
                    "Characteristic_Type": "Notes",
                    "Nominal": text_content,
                    "Tolerance_1": "",
                    "Tolerance_2": "",
                    "Upper Limit": "",
                    "Lower Limit": "",
                    "Dimension_Type": "",
                    "Repeat": "",
                    "dr_no": dr_value,
                    "Special Character Symbol": special_char_symbol,
                    "Note Number": ""
                }
                if is_this_note_number is not None:
                    text_measurement["Is_this_Note_Number"] = is_this_note_number
                merged_list.append(text_measurement)
                logging.info(f"Added separate text entry for Serial {serial_no}")
        else:
            # Create new text measurement entry
            text_measurement = {
                "Serial_number": serial_no,
                "Characteristic_Type": "Notes",
                "Nominal": text_content,
                "Tolerance_1": "",
                "Tolerance_2": "",
                "Upper Limit": "",
                "Lower Limit": "",
                "Dimension_Type": "",
                "Repeat": "",
                "dr_no": dr_value,
                "Special Character Symbol": special_char_symbol,
                "Note Number": ""
            }
            if is_this_note_number is not None:
                text_measurement["Is_this_Note_Number"] = is_this_note_number
            merged_list.append(text_measurement)
            logging.info(f"Added text measurement for Serial {serial_no}")

    logging.info(f"Merged {len(text_details)} text items into measurements: total {len(merged_list)} entries")
    return merged_list

def measurement_signature(m):
    """
    Build a content-based signature for a measurement so identical rows can be
    detected even when numeric formatting differs ('178' vs '178.0').
    """
    def _norm(v):
        s = str(v).strip()
        try:
            return repr(float(s))
        except (ValueError, TypeError):
            return s.lower()

    return (
        str(m.get("Serial_number", "")).strip().lower(),
        _norm(m.get("Nominal", "")),
        str(m.get("Characteristic_Type", "")).strip().lower(),
        _norm(m.get("Tolerance_1", "")),
        _norm(m.get("Tolerance_2", "")),
        _norm(m.get("Upper Limit", "") or m.get("Upper_Limit", "")),
        _norm(m.get("Lower Limit", "") or m.get("Lower_Limit", "")),
        str(m.get("Dimension_Type", "")).strip().lower(),
    )


def dedup_unserialized_measurements(measurements):
    """
    Remove duplicate rows that have no serial number (or are Text rows) and share
    the same content signature. Such duplicates arise when overlapping crops or
    split images extract the same standalone dimension/text multiple times.
    Rows with a real serial number are left untouched (serial dedup is separate).
    """
    if not measurements:
        return measurements

    seen = set()
    result = []
    removed = 0
    for m in measurements:
        serial = str(m.get("Serial_number", "")).strip().lower()
        char_type = str(m.get("Characteristic_Type", "")).strip().lower()
        if serial and serial != "not found" and char_type != "text":
            result.append(m)
            continue
        sig = measurement_signature(m)
        if sig in seen:
            removed += 1
            continue
        seen.add(sig)
        result.append(m)

    if removed:
        logging.info(f"Removed {removed} duplicate unserialized/text measurement rows")
    return result


def deduplicate_measurements_by_serial(measurements):
    """
    Remove duplicate serial numbers from measurements list, keeping only the first occurrence.

    Args:
        measurements: List of measurement dictionaries

    Returns:
        List of measurements with unique serial numbers (first occurrence kept)
    """
    seen_serials = set()
    deduplicated = []
    duplicates_count = 0

    for measurement in measurements:
        serial_number = measurement.get("Serial_number", "")

        # Skip empty serial numbers from deduplication check
        if not serial_number or str(serial_number).strip() == "" or str(serial_number).lower() == "not found":
            deduplicated.append(measurement)
            continue

        # Check if we've seen this serial number before
        if serial_number not in seen_serials:
            seen_serials.add(serial_number)
            deduplicated.append(measurement)
        else:
            duplicates_count += 1
            logging.warning(f"Duplicate serial number '{serial_number}' found and discarded (keeping first occurrence)")

    if duplicates_count > 0:
        logging.info(f"Removed {duplicates_count} duplicate serial number(s) from {len(measurements)} measurements")
    else:
        logging.info(f"No duplicates found in {len(measurements)} measurements")

    return deduplicated


def expand_repeated_measurements(measurements):
    """
    Expand measurements with Repeat > 1 into multiple measurements with A-Z suffixes.

    For example:
    - Serial_number: "3", Repeat: "2" → Creates "3A", "3B" (removes original "3")
    - Serial_number: "6", Repeat: "4" → Creates "6A", "6B", "6C", "6D" (removes original "6")

    Args:
        measurements: List of measurement dictionaries

    Returns:
        List of expanded measurements with suffixed serial numbers
    """
    # Suffix expansion disabled - measurements are returned without modification.
    # To re-enable suffix generation (e.g., 3A, 3B, 3C), uncomment the code below.
    logging.info(f"Expanded {len(measurements)} measurements to {len(measurements)} measurements")
    return measurements

    # --- Commented out expansion logic (kept for future use) ---
    # expanded_measurements = []
    #
    # for measurement in measurements:
    #     repeat_value = measurement.get("Repeat", "")
    #
    #     # Check if Repeat field exists and has a value > 1
    #     if repeat_value and repeat_value != "":
    #         try:
    #             repeat_count = int(repeat_value)
    #
    #             if repeat_count > 1:
    #                 serial_number = measurement.get("Serial_number", "")
    #
    #                 if not serial_number:
    #                     logging.warning("Measurement has Repeat but no Serial_number, skipping expansion")
    #                     expanded_measurements.append(measurement)
    #                     continue
    #
    #                 # Create duplicates with A-Z suffixes
    #                 for i in range(repeat_count):
    #                     if i < 26:
    #                         suffix = chr(65 + i)  # A-Z
    #                     else:
    #                         first_letter = chr(65 + (i // 26) - 1)
    #                         second_letter = chr(65 + (i % 26))
    #                         suffix = first_letter + second_letter
    #
    #                     duplicated_measurement = measurement.copy()
    #                     duplicated_measurement["Serial_number"] = f"{serial_number}{suffix}"
    #                     expanded_measurements.append(duplicated_measurement)
    #
    #                 logging.info(f"Expanded Serial {serial_number} with Repeat={repeat_count} into {repeat_count} measurements with suffixes")
    #
    #             else:
    #                 expanded_measurements.append(measurement)
    #
    #         except (ValueError, TypeError) as e:
    #             logging.warning(f"Invalid Repeat value '{repeat_value}' for Serial {measurement.get('Serial_number')}: {e}")
    #             expanded_measurements.append(measurement)
    #     else:
    #         expanded_measurements.append(measurement)
    #
    # logging.info(f"Expanded {len(measurements)} measurements to {len(expanded_measurements)} measurements")
    # return expanded_measurements

def cleanup_parents(measurements):
    """
    Remove parent measurements if suffixed versions exist.
    For example, if '20A' exists, remove '20'.
    """
    if not measurements:
        return measurements

    serials = {str(m.get("Serial_number", "")) for m in measurements if m.get("Serial_number")}

    parents_to_remove = set()
    for s in serials:
        if not s: continue
        if s + "A" in serials:
            parents_to_remove.add(s)

    if not parents_to_remove:
        return measurements

    logging.info(f"Removing parent measurements because suffixed versions exist: {parents_to_remove}")
    return [m for m in measurements if str(m.get("Serial_number", "")) not in parents_to_remove]

def process_bubble_data(extracted_data_list, header_data=None):
    """Process data extracted from bubbles on engineering drawings."""
    combined_data = []
    logging.info(f"Processing {len(extracted_data_list)} bubble data items")

    # Parse tolerance rules from header
    tolerance_rules = parse_tolerance_table(header_data)
    logging.info(f"Tolerance rules extracted: {tolerance_rules}")

    # Parse governing standard (ASME vs ISO) from header
    governing_standard_info = parse_governing_standard(header_data)
    if governing_standard_info.get('standard'):
        logging.info(f"Governing standard: {governing_standard_info['full_designation']} "
                     f"(Envelope Rule: {governing_standard_info['envelope_rule']})")

    for idx, data in enumerate(extracted_data_list):
        if data:
            try:
                # Parse the JSON data
                data_dict = json.loads(data)

                # Check if 'measurements' exists in the data
                if "measurements" in data_dict:
                    measurements = data_dict["measurements"]
                    logging.debug(f"Found {len(measurements)} measurements in item {idx}")

                    # Process each measurement
                    for measurement in measurements:
                        # Ensure all required fields exist with default values if missing
                        # Accept both underscore and space versions for compatibility
                        measurement_with_defaults = {
                            "Serial_number": measurement.get("Serial_number", ""),
                            "Characteristic_Type": measurement.get("Characteristic_Type", ""),
                            "Nominal": measurement.get("Nominal", ""),
                            "Tolerance_1": measurement.get("Tolerance_1", ""),
                            "Tolerance_2": measurement.get("Tolerance_2", ""),
                            "Upper Limit": measurement.get("Upper_Limit", "") or measurement.get("Upper Limit", ""),
                            "Lower Limit": measurement.get("Lower_Limit", "") or measurement.get("Lower Limit", ""),
                            "Dimension_Type": measurement.get("Dimension_Type", ""),
                            "Repeat": measurement.get("Repeat", ""),
                            "dr_no": measurement.get("dr_no", ""),
                            "has_bubble_number": measurement.get("has_bubble_number", "no"),
                            "GDT_Symbol": measurement.get("GDT_Symbol", ""),
                            "Note Number": measurement.get("Note Number", "")
                        }
                        if "bbox" in measurement:
                            measurement_with_defaults["bbox"] = measurement["bbox"]

                        original_dr = measurement_with_defaults["dr_no"]
                        measurement_with_defaults["Special Character Symbol"] = original_dr if original_dr else ""

                        # Verify datum references
                        dr_value = measurement_with_defaults["dr_no"]
                        if dr_value:
                            if  dr_value != "1":
                                logging.debug(f"Non-standard dr_no detected: {dr_value} in processing")
                                measurement_with_defaults["dr_no"] = "1"
                                logging.debug(f"Converted non-standard dr_no {dr_value} to 1 in processing")

                        # Apply tolerance logic
                        measurement_with_tolerance = apply_tolerance_to_measurement(
                            measurement_with_defaults,
                            tolerance_rules
                        )

                        # Apply metrological enrichment (adds optional metadata fields,
                        # never overwrites existing fields)
                        measurement_with_tolerance = enrich_measurement_metadata(
                            measurement_with_tolerance,
                            governing_standard_info
                        )

                        combined_data.append(measurement_with_tolerance)

            except json.JSONDecodeError:
                logging.warning(f"Invalid JSON format in bubble data #{idx}")
                continue
            except Exception as e:
                logging.exception(f"Error processing bubble data #{idx}: {e}")
                continue

    # Remove duplicate serial numbers, keeping the first occurrence
    deduplicated_data = deduplicate_measurements_by_serial(combined_data)

    # Expand measurements with Repeat > 1
    expanded_data = expand_repeated_measurements(deduplicated_data)

    logging.info(f"Processed {len(expanded_data)} total measurements (after expansion) with tolerance applied")
    return expanded_data



def format_text(extracted_text_list):
    try:
        if not extracted_text_list or not extracted_text_list[0]:
            raise ValueError("Input list is empty.")

        parsed_data = json.loads(extracted_text_list[0])

        if isinstance(parsed_data, list):
            formatted_data = []
            for item in parsed_data:
                if isinstance(item, dict):
                    formatted_data.append({
                        "serial_no": str(item.get("serial_no", "Not Found")),
                        "text": str(item.get("text", "")),
                        "dr_no": str(item.get("dr_no", "")),
                        "Is_this_Note_Number": item.get("Is_this_Note_Number")
                    })
            return json.dumps(formatted_data, indent=4)
        elif isinstance(parsed_data, dict):
            formatted_data = []
            for key, value in parsed_data.items():
                if isinstance(value, dict):
                    formatted_data.append({"serial_no": str(key), "text": str(value.get("text", "")), "dr_no": str(value.get("dr_no", "")), "Is_this_Note_Number": value.get("Is_this_Note_Number")})
                else:
                    formatted_data.append({"serial_no": str(key), "text": str(value), "dr_no": "", "Is_this_Note_Number": None})
            return json.dumps(formatted_data, indent=4)
        else:
            raise ValueError("Extracted text is not a valid dictionary or list format.")

    except (IndexError, json.JSONDecodeError, ValueError) as e:
        return json.dumps({"error": str(e)}, indent=4)


def process_text(extracted_text_list):
    """
    Process extracted text from multiple images and combine into a list format.
    Handles both valid JSON strings and error cases, including nested stringified JSON from Gemini.
    Returns data in the format: [{"serial_no": "key", "text": "value", "dr_no": "val"}, ...]
    """
    combined_items = []

    if not extracted_text_list:
        return []

    for text_str in extracted_text_list:
        if not text_str:
            continue

        try:
            # Attempt to load the initial string as JSON
            text_data = json.loads(text_str) if isinstance(text_str, str) else text_str

            # Handle the case where Gemini returns a dictionary with a single key
            if isinstance(text_data, dict) and len(text_data) == 1:
                key, value = next(iter(text_data.items()))
                if isinstance(value, str):
                    try:
                        nested_data = eval(value)
                        text_data = nested_data
                    except (SyntaxError, NameError, TypeError):
                        text_data = {key: value}
                elif isinstance(value, list) or isinstance(value, dict):
                    text_data = value

            # Standard processing for list format
            if isinstance(text_data, list):
                for item in text_data:
                    if isinstance(item, dict):
                        serial_no = str(item.get("serial_no", "Not Found"))
                        text_content = item.get("text", "")
                        is_this_note_number = item.get("Is_this_Note_Number")
                        if isinstance(text_content, dict):
                            dr_no = str(text_content.get("dr_no", ""))
                            text_content = str(text_content.get("text", ""))
                        else:
                            dr_no = str(item.get("dr_no", ""))

                        if not str(text_content).strip():
                            continue

                        if serial_no.isdigit():
                            existing = next((x for x in combined_items if x["serial_no"] == serial_no), None)
                            if existing:
                                existing["text"] += "\n" + str(text_content)
                                if dr_no and not existing["dr_no"]:
                                    existing["dr_no"] = dr_no
                                if is_this_note_number is not None:
                                    existing["Is_this_Note_Number"] = is_this_note_number
                            else:
                                combined_items.append({"serial_no": serial_no, "text": str(text_content), "dr_no": dr_no, "Is_this_Note_Number": is_this_note_number})
                        else:
                            combined_items.append({"serial_no": "Not Found", "text": str(text_content), "dr_no": dr_no, "Is_this_Note_Number": is_this_note_number})

            # Standard processing for dict format fallback
            elif isinstance(text_data, dict):
                for key, value in text_data.items():
                    text_content = value
                    dr_no = ""
                    is_this_note_number = None
                    if isinstance(value, dict):
                        text_content = value.get("text", "")
                        dr_no = str(value.get("dr_no", ""))
                        is_this_note_number = value.get("Is_this_Note_Number")

                    if not str(text_content).strip():
                        continue

                    serial_no = str(key)
                    if serial_no.isdigit():
                        existing = next((x for x in combined_items if x["serial_no"] == serial_no), None)
                        if existing:
                            existing["text"] += "\n" + str(text_content)
                            if dr_no and not existing["dr_no"]:
                                existing["dr_no"] = dr_no
                            if is_this_note_number is not None:
                                existing["Is_this_Note_Number"] = is_this_note_number
                        else:
                            combined_items.append({"serial_no": serial_no, "text": str(text_content), "dr_no": dr_no, "Is_this_Note_Number": is_this_note_number})
                    else:
                        combined_items.append({"serial_no": "Not Found", "text": str(text_content), "dr_no": dr_no, "Is_this_Note_Number": is_this_note_number})
            else:
                logging.warning(f"Extracted text is not a dictionary or list: {type(text_data)}")

        except json.JSONDecodeError as e:
            logging.error(f"Failed to parse text as JSON: {e}")
            logging.debug(f"Problematic text: {text_str[:200]}...")
        except Exception as e:
            logging.error(f"Error processing text item: {e}")

    # Return sorting digit keys first, then "Not Found" items
    digit_items = sorted([item for item in combined_items if item["serial_no"].isdigit()], key=lambda x: int(x["serial_no"]))
    unkeyed_items = [item for item in combined_items if not item["serial_no"].isdigit()]

    return digit_items + unkeyed_items


def _is_missing(value):
    """Check if a header field value is considered missing/empty."""
    if value is None:
        return True
    if isinstance(value, str) and (value.strip() == "" or value.strip().lower() == "not found"):
        return True
    return False


def enrich_header_from_full_scan(header_data, fallback_data):
    """
    Enrich header_data with tolerance and material fields from a full-image
    fallback scan.  Only fills fields that are currently missing / empty /
    'Not Found'.  Never overwrites existing valid header values.

    Args:
        header_data: The header dict extracted from class-0 YOLO regions.
                     May be None if YOLO failed to detect a header.
        fallback_data: Dict with Material, Alt. Material, and Tolerance
                       fields extracted from the full-image LLM scan.

    Returns:
        The enriched header_data dict.
    """
    # Defensive normalization of header_data to dict
    if header_data is None:
        header_data = {}
    elif isinstance(header_data, list):
        merged = {}
        for item in header_data:
            if isinstance(item, dict):
                for k, v in item.items():
                    if k not in merged or _is_missing(merged[k]):
                        merged[k] = v
        header_data = merged
    elif not isinstance(header_data, dict):
        header_data = {}

    # Defensive normalization of fallback_data to dict
    if isinstance(fallback_data, list):
        if len(fallback_data) > 0 and isinstance(fallback_data[0], dict):
            fallback_data = fallback_data[0]
        else:
            fallback_data = {}
    elif not isinstance(fallback_data, dict):
        fallback_data = {}

    if not fallback_data:
        logging.info("No fallback data provided for header enrichment")
        return header_data

    fields_enriched = []

    # --- Material ---
    if _is_missing(header_data.get("Material")):
        fb_material = fallback_data.get("Material")
        if not _is_missing(fb_material):
            header_data["Material"] = fb_material
            fields_enriched.append(f"Material='{fb_material}'")

    # --- Alt. Material ---
    if _is_missing(header_data.get("Alt. Material")):
        fb_alt_material = fallback_data.get("Alt. Material")
        if not _is_missing(fb_alt_material):
            header_data["Alt. Material"] = fb_alt_material
            fields_enriched.append(f"Alt. Material='{fb_alt_material}'")

    # --- Coating Finish / Hardness / Heat Treatment ---
    for field in ("Coating Finish", "Hardness", "Heat Treatment"):
        if _is_missing(header_data.get(field)):
            fb_value = fallback_data.get(field)
            if not _is_missing(fb_value):
                header_data[field] = fb_value
                fields_enriched.append(f"{field}='{fb_value}'")

    # --- Revision Number ---
    if _is_missing(header_data.get("Revision Number")):
        fb_revision_number = fallback_data.get("Revision Number")
        if not _is_missing(fb_revision_number):
            header_data["Revision Number"] = fb_revision_number
            fields_enriched.append(f"Revision Number='{fb_revision_number}'")

    # --- Latest Revision Date ---
    if _is_missing(header_data.get("latest_revision_date")):
        fb_latest_revision_date = fallback_data.get("latest_revision_date")
        if not _is_missing(fb_latest_revision_date):
            header_data["latest_revision_date"] = fb_latest_revision_date
            fields_enriched.append(f"latest_revision_date='{fb_latest_revision_date}'")

    # --- Tolerance (nested dict) ---
    fb_tolerance = fallback_data.get("Tolerance")
    if fb_tolerance and isinstance(fb_tolerance, dict):
        if "Tolerance" not in header_data or not isinstance(header_data.get("Tolerance"), dict):
            header_data["Tolerance"] = {}

        header_tol = header_data["Tolerance"]

        tolerance_fields = [
            "Whole Number",
            "One Place Decimal",
            "Two Place Decimal",
            "Three Place Decimal",
            "Four Place Decimal",
            "Angular",
            "Fractional",
            "Linear",
            "Diametral",
        ]

        for field in tolerance_fields:
            if _is_missing(header_tol.get(field)):
                fb_value = fb_tolerance.get(field)
                if not _is_missing(fb_value):
                    header_tol[field] = fb_value
                    fields_enriched.append(f"Tolerance.{field}='{fb_value}'")

        # --- Ranges ---
        header_ranges = header_tol.get("Ranges", [])
        fb_ranges = fb_tolerance.get("Ranges", [])
        if (not header_ranges or header_ranges == []) and fb_ranges and isinstance(fb_ranges, list) and len(fb_ranges) > 0:
            # Only adopt fallback ranges if header has none
            header_tol["Ranges"] = fb_ranges
            fields_enriched.append(f"Tolerance.Ranges ({len(fb_ranges)} range(s))")

    if fields_enriched:
        logging.info(f"Header enriched from full-image fallback scan: {', '.join(fields_enriched)}")
    else:
        logging.info("No additional fields enriched from fallback scan (header already complete or fallback had nothing new)")

    return header_data


def _is_see_notes_reference(value):
    """Check if a header field value is a 'see notes' reference rather than an actual material."""
    if not value or not isinstance(value, str):
        return False
    normalized = value.strip().upper()
    # Common patterns: "SEE NOTES", "SEE NOTE 3", "SEE NOTE #3", "REFER TO NOTES",
    # "PER NOTE 5", "AS PER NOTES", "SEE GENERAL NOTES", "NOTE 7", "REFER NOTE",
    # "SEE TEXT", etc.
    see_notes_patterns = [
        r'\bSEE\s+NOTE',
        r'\bSEE\s+GENERAL\s+NOTE',
        r'\bSEE\s+TEXT',
        r'\bREFER\s+(?:TO\s+)?NOTE',
        r'\bPER\s+NOTE',
        r'\bAS\s+PER\s+NOTE',
        r'^NOTE\s*[#]?\s*\d',
    ]
    for pattern in see_notes_patterns:
        if re.search(pattern, normalized):
            return True
    return False


def _extract_material_from_text(text_content):
    """
    Try to extract a material specification from a text string.
    Returns the material string if found, or None.
    """
    if not text_content or not isinstance(text_content, str):
        return None

    text_upper = text_content.upper().strip()

    # Pattern 1: Explicit "MATERIAL:" or "MAT'L:" or "MATL:" prefix
    mat_prefix_pattern = r'(?:MATERIAL|MAT\'?L|MATL)\s*[:=]\s*(.+?)(?:\.|$)'
    match = re.search(mat_prefix_pattern, text_content, re.IGNORECASE)
    if match:
        material = match.group(1).strip()
        # Don't return if it's another "see notes" reference
        if material and not _is_see_notes_reference(material):
            return material

    # Pattern 2: Common material designations appearing in the text
    # These patterns match well-known material specifications
    material_patterns = [
        # ASTM/SAE/AISI/AMS material specs
        r'((?:ASTM|SAE|AISI|AMS|ANSI|MIL|QQ)\s*[-]?\s*[A-Z0-9][-A-Z0-9./\s]{2,})',
        # Aluminum alloys (e.g., 6061-T6, AA 6020, 2024-T3)
        r'((?:AA\s*)?(?:6\d{3}|7\d{3}|2\d{3}|5\d{3}|1\d{3})[-\s]*(?:T\d+|H\d+|O)?(?:\s+ALUMINUM)?)',
        # Steel designations (e.g., 1018, 4140, 304 SS, A36)
        r'((?:STEEL|STAINLESS\s+STEEL|SS)\s+(?:SAE\s+)?\d{3,5}[-A-Z0-9]*)',
        r'(\d{3,4}\s+(?:STEEL|SS|STAINLESS))',
        # Common named materials
        r'((?:BRASS|BRONZE|COPPER|TITANIUM|NYLON|DELRIN|PEEK|PTFE|TEFLON|INCONEL|HASTELLOY|MONEL)\s*(?:CDA\s*)?\d*[-A-Z0-9]*)',
    ]

    for pattern in material_patterns:
        match = re.search(pattern, text_content, re.IGNORECASE)
        if match:
            material = match.group(1).strip()
            if len(material) >= 3:  # Minimum reasonable material name length
                return material

    return None


def resolve_see_notes_material(header_data, text_details, fallback_data=None):
    """
    Resolve 'See Notes' material references in the header by searching
    the extracted text details (notes/text blocks) for the actual material.

    Engineering drawings sometimes specify 'SEE NOTES' or 'SEE NOTE 3' in the
    title block's Material field. The actual material specification is then
    found in the numbered notes section or general notes area of the drawing.

    This function:
    1. Checks if Material (or Alt. Material) contains a 'see notes' reference
    2. Scans text_details for material-related content
    3. Falls back to tolerance_material_fallback data if text doesn't help
    4. Replaces the 'see notes' placeholder with the resolved material

    Args:
        header_data: The header dict (may be modified in-place).
        text_details: List of dicts with format [{"serial_no": ..., "text": ...}, ...]
        fallback_data: Optional dict from full-image tolerance/material scan.

    Returns:
        The header_data dict with resolved material fields.
    """
    if not header_data or not isinstance(header_data, dict):
        return header_data

    fields_to_check = ["Material", "Alt. Material"]
    resolved_fields = []

    for field in fields_to_check:
        current_value = header_data.get(field, "")
        if not _is_see_notes_reference(current_value):
            continue

        logging.info(f"Header '{field}' contains a 'see notes' reference: '{current_value}' — scanning text details for actual material")

        # Try to extract a specific note number from the reference (e.g., "SEE NOTE 3" → 3)
        target_note_number = None
        note_num_match = re.search(r'NOTE\s*[#]?\s*(\d+)', current_value, re.IGNORECASE)
        if note_num_match:
            target_note_number = note_num_match.group(1)
            logging.info(f"  Target note number extracted: {target_note_number}")

        resolved_material = None

        # Search text_details for material information
        if text_details:
            # Priority 1: If a specific note number is referenced, check that note first
            if target_note_number:
                for text_item in text_details:
                    serial_no = str(text_item.get("serial_no", "")).strip()
                    if serial_no == target_note_number:
                        material = _extract_material_from_text(text_item.get("text", ""))
                        if material:
                            resolved_material = material
                            logging.info(f"  Resolved '{field}' from note #{target_note_number}: '{material}'")
                            break
                        # Even if no structured material pattern found, if the text
                        # content of the referenced note looks like a material spec
                        # (short, no obvious non-material content), use it directly
                        text_content = text_item.get("text", "").strip()
                        if text_content and len(text_content) < 200:
                            # Check if it contains material-related keywords
                            mat_keywords = [
                                'MATERIAL', 'MAT\'L', 'MATL', 'STEEL', 'ALUMINUM',
                                'BRASS', 'COPPER', 'TITANIUM', 'NYLON', 'PLASTIC',
                                'STAINLESS', 'ALLOY', 'TEMPER', 'ASTM', 'SAE',
                                'AISI', 'AMS', 'CAST', 'IRON', 'BRONZE',
                            ]
                            if any(kw in text_content.upper() for kw in mat_keywords):
                                # Strip common prefixes like "MATERIAL:" from the text
                                cleaned = re.sub(
                                    r'^(?:MATERIAL|MAT\'?L|MATL)\s*[:=]\s*',
                                    '', text_content, flags=re.IGNORECASE
                                ).strip()
                                if cleaned and not _is_see_notes_reference(cleaned):
                                    resolved_material = cleaned
                                    logging.info(f"  Resolved '{field}' from note #{target_note_number} (keyword match): '{cleaned}'")
                                    break

            # Priority 2: Scan all text items for material references
            if not resolved_material:
                for text_item in text_details:
                    text_content = text_item.get("text", "")
                    material = _extract_material_from_text(text_content)
                    if material:
                        resolved_material = material
                        serial_no = text_item.get("serial_no", "N/A")
                        logging.info(f"  Resolved '{field}' from text (serial {serial_no}): '{material}'")
                        break

        # Priority 3: Fallback to full-image scan data
        if not resolved_material and fallback_data:
            fb_value = fallback_data.get(field)
            if fb_value and not _is_missing(fb_value) and not _is_see_notes_reference(str(fb_value)):
                resolved_material = fb_value
                logging.info(f"  Resolved '{field}' from tolerance/material fallback scan: '{fb_value}'")

        # Apply the resolved material
        if resolved_material:
            header_data[field] = resolved_material
            resolved_fields.append(f"{field}='{resolved_material}' (was: '{current_value}')")
        else:
            logging.warning(f"  Could not resolve '{field}' from text details or fallback — keeping original value: '{current_value}'")

    if resolved_fields:
        logging.info(f"Resolved 'see notes' material references: {', '.join(resolved_fields)}")
    else:
        logging.debug("No 'see notes' material references found in header")

    return header_data

def merge_title_block_bubbles_into_measurements(measurements, header_data):
    """
    Merge title block bubbles found in the header into the measurements list.
    """
    if not header_data or not isinstance(header_data, dict):
        return measurements

    title_block_bubbles = header_data.get("Title_Block_Bubbles", [])
    if not title_block_bubbles or not isinstance(title_block_bubbles, list):
        return measurements

    added_count = 0
    for bubble in title_block_bubbles:
        if not isinstance(bubble, dict):
            continue

        serial = str(bubble.get("Serial_number", "")).strip()
        if not serial or serial.lower() == "not found":
            continue

        field = str(bubble.get("Field", "")).strip()
        value = str(bubble.get("Value", "")).strip()

        # Ensure we don't duplicate if a bubble with this serial already exists
        exists = any(str(m.get("Serial_number", "")).strip() == serial for m in measurements)
        if not exists:
            new_measurement = {
                "Serial_number": serial,
                "Characteristic_Type": field if field else "Notes",
                "Nominal": value,
                "Tolerance_1": "",
                "Tolerance_2": "",
                "Upper Limit": "",
                "Lower Limit": "",
                "Dimension_Type": "",
                "Repeat": "1",
                "dr_no": "",
                "has_bubble_number": "yes",
                "GDT_Symbol": "",
                "Note Number": ""
            }
            measurements.append(new_measurement)
            added_count += 1
            logging.info(f"Added title block bubble measurement: Serial {serial} ({field})")

    if added_count > 0:
        logging.info(f"Merged {added_count} title block bubbles into measurements")

    return measurements


# Canonical title-block fields that must always surface as measurement rows.
MATERIAL_INFO_FIELDS = ("Material", "Coating Finish", "Hardness", "Heat Treatment")

# Normalized label aliases used to map Title_Block_Bubbles "Field" values and
# header keys onto the canonical field names above.
_MATERIAL_FIELD_ALIASES = {
    "Material": ("material", "matl", "materialspec", "materialspecification"),
    "Coating Finish": ("coatingfinish", "coating", "finish", "plating", "coat"),
    "Hardness": ("hardness",),
    "Heat Treatment": ("heattreatment", "heattreat", "heattreating"),
}

# Labels containing any of these fragments never map to a canonical field —
# prevents "Alt. Material" / "Optional Material" from matching "Material".
_MATERIAL_FIELD_EXCLUSIONS = ("alt", "alternate", "optional")


def _normalize_field_label(value):
    """Lowercase, alphanumeric-only form of a field label for tolerant matching."""
    return re.sub(r"[^a-z0-9]", "", str(value or "").lower())


def _canonical_material_field(label):
    """Map an arbitrary field label onto a canonical material-block field name."""
    norm = _normalize_field_label(label)
    if not norm or any(ex in norm for ex in _MATERIAL_FIELD_EXCLUSIONS):
        return None
    for canonical, aliases in _MATERIAL_FIELD_ALIASES.items():
        if norm == _normalize_field_label(canonical) or any(alias in norm for alias in aliases):
            return canonical
    return None


def _norm_material_value(value):
    """Normalize a value for comparison ('178' == '178.0' == ' 178 ')."""
    s = str(value or "").strip().lower()
    try:
        return repr(float(s))
    except (ValueError, TypeError):
        return s


def _get_header_field_value(header_data, canonical_field):
    """
    Fetch a header value by canonical field name, tolerating key variations
    (e.g. 'COATING FINISH', 'Coating_Finish'). Prefers a non-missing value
    when several keys normalize to the same field.
    """
    values = [
        v for k, v in header_data.items()
        if _canonical_material_field(k) == canonical_field
    ]
    for v in values:
        if not _is_missing(v):
            return v
    return values[0] if values else None


def merge_material_info_into_measurements(measurements, header_data):
    """
    Ensure MATERIAL, COATING FINISH, HARDNESS and HEAT TREATMENT appear in the
    measurements list.

    For each field found in the header:
    - Serial_number is taken from header Title_Block_Bubbles when a bubble was
      extracted for that field; otherwise it is left empty.
    - If a measurement row already represents the field (same bubble serial or
      same field/value pair), the existing row is backfilled rather than
      duplicated.
    """
    if measurements is None:
        measurements = []
    if not header_data or not isinstance(header_data, dict):
        return measurements

    # Map canonical field -> bubble serial from Title_Block_Bubbles
    field_serials = {}
    bubbles = header_data.get("Title_Block_Bubbles", [])
    if isinstance(bubbles, list):
        for b in bubbles:
            if not isinstance(b, dict):
                continue
            canonical = _canonical_material_field(b.get("Field", ""))
            serial = str(b.get("Serial_number", "")).strip()
            if canonical and serial and serial.lower() != "not found" and canonical not in field_serials:
                field_serials[canonical] = serial

    def _find_existing(serial, field, value):
        norm_value = _norm_material_value(value)
        for m in measurements:
            if not isinstance(m, dict):
                continue
            m_field = _canonical_material_field(m.get("Characteristic_Type", ""))
            m_nom = _norm_material_value(m.get("Nominal", ""))
            # Row for this field already exists (e.g. added by the
            # title-block-bubble merge) — same field, same or empty nominal.
            if m_field == field and (m_nom == norm_value or _is_missing(m.get("Nominal"))):
                return m
            # A row already carries this serial with this exact value and is not
            # attributed to a different canonical field.
            if (
                serial
                and str(m.get("Serial_number", "")).strip() == serial
                and m_nom == norm_value
                and m_field in (None, field)
            ):
                return m
        return None

    added_count = 0
    for field in MATERIAL_INFO_FIELDS:
        value = _get_header_field_value(header_data, field)
        if _is_missing(value):
            continue
        value = str(value).strip()
        serial = field_serials.get(field, "")

        existing = _find_existing(serial, field, value)
        if existing is not None:
            if _is_missing(existing.get("Nominal")):
                existing["Nominal"] = value
            if not _canonical_material_field(existing.get("Characteristic_Type", "")):
                existing["Characteristic_Type"] = field
            if serial and not str(existing.get("Serial_number", "")).strip():
                existing["Serial_number"] = serial
            continue

        measurements.append({
            "Serial_number": serial,
            "Characteristic_Type": field,
            "Nominal": value,
            "Tolerance_1": "",
            "Tolerance_2": "",
            "Upper Limit": "",
            "Lower Limit": "",
            "Dimension_Type": "",
            "Repeat": "1",
            "dr_no": "",
            "has_bubble_number": "yes" if serial else "no",
            "GDT_Symbol": "",
            "Note Number": "",
        })
        added_count += 1
        logging.info(f"Added material-block measurement: {field} = '{value}' (serial: '{serial or 'none'}')")

    if added_count > 0:
        logging.info(f"Merged {added_count} material-block field(s) into measurements")

    return measurements
