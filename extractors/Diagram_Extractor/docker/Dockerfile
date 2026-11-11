FROM python:3.11-slim

WORKDIR /app

# Set environment variables
ENV TZ=Asia/Kolkata \
    PIP_ROOT_USER_ACTION=ignore

# System dependencies
RUN apt-get update && \
    apt-get install --no-install-recommends -y \
    libgl1 libjpeg-dev libsm6 libxext6 poppler-utils \
    libgl1-mesa-glx ibglib2.0-0 libxrender1 curl gnupg2 \
    unixodbc-dev gcc g++ && \
    curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | apt-key add - && \
    curl -fsSL https://packages.microsoft.com/config/debian/10/prod.list > /etc/apt/sources.list.d/mssql-release.list && \
    apt-get update && \
    ACCEPT_EULA=Y apt-get install -y msodbcsql17 && \
    rm -rf /var/lib/apt/lists/*

# Install Python packages
COPY requirements.txt .
RUN python -m pip install --upgrade pip && \
    pip install --no-cache-dir -r requirements.txt

# Copy the full application source
COPY . .


EXPOSE 8000

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
