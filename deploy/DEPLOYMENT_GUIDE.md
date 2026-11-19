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
   cp /opt/faceattendance/deploy/.env.example .env  # edit values (FACE_API_KEY is required)
   chmod 600 .env

5. ASP.NET build:
   cd /opt/faceattendance
   cp deploy/.env.example .env && chmod 600 .env   # set FaceRecognition__ApiKey (same value as FACE_API_KEY)
                                                   # and Admin__InitialUsername / Admin__InitialPassword
   dotnet ef database update                       # applies AddSecurityHardening (lockout + audit columns)
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
   # FastAPI not publicly exposed (127.0.0.1 only) and rejects calls without X-Api-Key

## Security notes
- No default admin exists. The first admin is created from Admin__InitialUsername /
  Admin__InitialPassword and must change the password at first login. Any existing admin
  still on the old default password is flagged at startup and blocked until it is changed.
- Logins lock for 15 minutes after 5 wrong passwords (admin and employee).
- Per-IP rate limits: 10/min on login, 30/min on check-in. The client IP comes from
  X-Forwarded-For, trusted only from the local nginx (127.0.0.1).
- Check-in requires a head-turn liveness challenge and a GPS fix (accuracy <= 100 m) inside an
  active location. Coordinates, matched location, distance and client IP are stored on each
  attendance record. A browser cannot prove GPS is not spoofed; requiring it, validating it on
  the server and logging it is the realistic mitigation.
- Remaining liveness gap: a replayed video of the right person turning the right way.
  A passive anti-spoof model (e.g. MiniFASNet ONNX) would cover that.
