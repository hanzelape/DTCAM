# Face Attendance System - Ubuntu VPS Deployment Guide

## Prerequisites
- Ubuntu 22.04/24.04
- .NET 8 SDK/runtime
- Python 3.11+
- MySQL 8
- Nginx
- certbot

## Steps
1. Create user and directories:
   sudo mkdir -p /opt/faceattendance
   sudo chown -R www-data:www-data /opt/faceattendance

2. Copy project to /opt/faceattendance (aspnet files at root, face-recognition-api subdir).

3. MySQL migration (preserves data):
   - Backup: mysqldump -u root -p face_attendance_db > backup.sql
   - Create user: CREATE USER 'faceapp'@'localhost' IDENTIFIED BY '...'; GRANT ALL ON face_attendance_db.* TO 'faceapp'@'localhost';
   - Restore if needed: mysql -u faceapp -p face_attendance_db < backup.sql

4. Python setup:
   cd /opt/faceattendance/face-recognition-api
   python3 -m venv venv
   source venv/bin/activate
   pip install -r requirements.txt
   cp /opt/faceattendance/deploy/.env.example .env  # edit values

5. ASP.NET build:
   cd /opt/faceattendance
   dotnet publish -c Release -o /opt/faceattendance/publish
   # update WorkingDirectory/ExecStart paths accordingly or use publish dir

6. Install systemd units:
   sudo cp deploy/systemd/*.service /etc/systemd/system/
   sudo systemctl daemon-reload
   sudo systemctl enable --now face-recognition.service
   sudo systemctl enable --now face-attendance.service

7. Nginx + HTTPS:
   sudo cp deploy/nginx/face-attendance.conf /etc/nginx/sites-available/face-attendance
   sudo ln -s /etc/nginx/sites-available/face-attendance /etc/nginx/sites-enabled/
   sudo nginx -t && sudo systemctl reload nginx
   sudo certbot --nginx -d YOUR_DOMAIN

8. Verify:
   sudo systemctl status face-recognition face-attendance
   curl https://YOUR_DOMAIN
   # FastAPI not publicly exposed (127.0.0.1 only)
