from PIL import Image, ImageEnhance, ImageFilter
import cv2
import numpy as np
from loguru import logger as logging

def preprocess_image(pil_img: Image.Image, 
                    apply_clahe: bool = True,
                    denoise: bool = True,
                    binarize: bool = False,
                    morphology: bool = False,
                    darken_light_text: bool = True) -> Image.Image:
    """
    Applies advanced image preprocessing techniques tailored for engineering drawings.
    
    Args:
        pil_img: The original PIL Image.
        apply_clahe: Apply Contrast Limited Adaptive Histogram Equalization for local contrast.
        denoise: Apply slight Gaussian blur to reduce scan noise before other steps.
        binarize: Apply adaptive thresholding to create a pure black & white image (good for removing gradients).
        morphology: Apply morphological closing to bridge small gaps in thin lines/text.
        darken_light_text: Specially thickens and darkens very faint/thin gray text against white backgrounds.
        
    Returns:
        Enhanced PIL.Image.Image.
    """
    logging.info(f"Preprocessing engineering drawing: clahe={apply_clahe}, denoise={denoise}, binarize={binarize}, morphology={morphology}, darken={darken_light_text}")
    
    # Convert PIL to cv2 (RGB to BGR)
    cv_img = np.array(pil_img)
    if len(cv_img.shape) == 3 and cv_img.shape[2] == 3:
        cv_img = cv2.cvtColor(cv_img, cv2.COLOR_RGB2BGR)

    # 1. Convert to grayscale (drawings are structure-heavy, color is often noise or unnecessary)
    if len(cv_img.shape) == 3:
        gray = cv2.cvtColor(cv_img, cv2.COLOR_BGR2GRAY)
    else:
        gray = cv_img.copy()

    # Custom handling for extremely light gray or thin text
    if darken_light_text:
        # 1a. Gamma correction to drop mid-tones heavily toward black while keeping white as white
        gamma = 2.5
        table = np.array([((i / 255.0) ** gamma) * 255 for i in np.arange(0, 256)]).astype("uint8")
        gray = cv2.LUT(gray, table)
        
        # 1b. Slight morphological erosion. 
        # In a white-background image, "erode" consumes the white pixels, making dark lines thicker and bolder.
        kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (2, 2))
        gray = cv2.erode(gray, kernel, iterations=1)

    # 2. Denoising (Gaussian blur for smoothing speckles without completely losing thin lines)
    if denoise:
        gray = cv2.GaussianBlur(gray, (3, 3), 0)

    # 3. CLAHE (Contrast Limited Adaptive Histogram Equalization)
    if apply_clahe:
        clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
        gray = clahe.apply(gray)

    # 4. Binarization (Adaptive thresholding is great for uneven lighting in scans)
    if binarize:
        # block size 21, C=10 - good for text and lines against white/grey bg
        gray = cv2.adaptiveThreshold(gray, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C, 
                                     cv2.THRESH_BINARY, 21, 10)

    # 5. Morphological operations (Closing - bridge small gaps in text and lines)
    if morphology:
        binary_inv = cv2.bitwise_not(gray) # morphology works on white shapes (foreground)
        kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (2, 2))
        binary_inv = cv2.morphologyEx(binary_inv, cv2.MORPH_CLOSE, kernel, iterations=1)
        gray = cv2.bitwise_not(binary_inv)

    # Convert back to RGB for PIL compatibility
    final_img = cv2.cvtColor(gray, cv2.COLOR_GRAY2RGB)
    return Image.fromarray(final_img)
