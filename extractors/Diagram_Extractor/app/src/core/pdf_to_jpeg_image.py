import fitz
import os
from pathlib import Path


def convert_pdf_to_images(pdf_path, output_folder, dpi=300, image_format="jpeg"):

    Path(output_folder).mkdir(parents=True, exist_ok=True)
    base_filename = os.path.splitext(os.path.basename(pdf_path))[0]
    pdf_document = fitz.open(pdf_path)
    zoom = dpi / 72  
    for page_num, page in enumerate(pdf_document):
        matrix = fitz.Matrix(zoom, zoom)
        pixmap = page.get_pixmap(matrix=matrix, alpha=False)
        output_file = os.path.join(output_folder, f"{base_filename}_page_{page_num+1}.{image_format}")
        pixmap.save(output_file)
    pdf_document.close()

if __name__ == "__main__":
    pdf_path = r"C:\Users\omnex\Downloads\STLA_P357_GEN2PIM_PRDP_ProjectDevelopmentPlan_400V.docx.PDF"
    output = r"C:\Users\omnex\Desktop\New folder"
    
    convert_pdf_to_images(pdf_path,output)