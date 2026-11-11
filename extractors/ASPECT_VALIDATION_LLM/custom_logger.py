import logging
import threading

_lock = threading.Lock()

def setup_logger(log_file_path):
    with _lock:
        # Use unique logger per file path
        logger_name = f'LLM-EXTRACTOR-{hash(log_file_path)}'
        logger = logging.getLogger(logger_name)

        # Only configure if not already configured
        if logger.hasHandlers():
            return logger

        logger.setLevel(logging.DEBUG)
        logger.propagate = False  # Critical for multiprocessing

        fh = logging.FileHandler(log_file_path, mode='a', encoding='utf-8')  # Append mode
        fh.setLevel(logging.DEBUG)

        ch = logging.StreamHandler()
        ch.setLevel(logging.ERROR)

        formatter = logging.Formatter('%(asctime)s - %(name)s - %(levelname)s - %(funcName)s - line %(lineno)d - %(message)s')
        fh.setFormatter(formatter)
        ch.setFormatter(formatter)

        logger.addHandler(fh)
        logger.addHandler(ch)

        return logger