import cv2
import numpy as np
import socket
import struct
import pickle
import time
import sys
from utils.mongodb_util import update_hydration_outputs  # Function to save data in MongoDB

# ---------------------------
# Client Setup - Connect to Frame Server
# ---------------------------
HOST = '127.0.0.1'
PORT = 9999

client_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
client_socket.connect((HOST, PORT))
data = b""

# ---------------------------
# Dry Lips Detection Settings
# ---------------------------
TEXTURE_THRESHOLD = 30     # Used to normalize the texture score
DRYNESS_THRESHOLD = 0.17   # If normalized score > 0.17, consider lips as dry

# Load Haar Cascade Models for Face and Mouth Detection
face_cascade = cv2.CascadeClassifier("C:\\Users\\chamu\\source\\repos\\EDUGuard_DesktopApp\\EDUGuard_DesktopApp\\PyFiles\\models\\haarcascade_frontalface_default.xml")
mouth_cascade = cv2.CascadeClassifier("C:\\Users\\chamu\\source\\repos\\EDUGuard_DesktopApp\\EDUGuard_DesktopApp\\PyFiles\\models\\haarcascade_mcs_mouth.xml")

# -------------------------------
# Get User Information from Arguments
# -------------------------------
if len(sys.argv) < 3:
    print("Error: Missing arguments (email, progress_report_id)")
    sys.exit(1)

USER_EMAIL = sys.argv[1]
progress_report_id = sys.argv[2]

# -------------------------------
# Timer Setup
# -------------------------------
save_interval = 1  # Save lip dryness status every 1 second
batch_interval = 30  # Save the batch to the database every 30 seconds
last_saved_time = time.time()
last_batch_time = time.time()
lip_dryness_batch = []  # Store lip dryness data before saving

# -------------------------------
# Function to Detect Lip Dryness
# -------------------------------
def detect_lip_dryness(frame):
    """
    Detect lips in the given frame and classify as 'Dry Lips' or 'Normal Lips'.
    """
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    faces = face_cascade.detectMultiScale(gray, scaleFactor=1.3, minNeighbors=5, minSize=(60, 60))
    dryness_label = None

    for (x, y, w, h) in faces:
        face_roi = gray[y:y+h, x:x+w]
        mouths = mouth_cascade.detectMultiScale(face_roi, scaleFactor=1.3, minNeighbors=8, minSize=(30, 30))

        for (mx, my, mw, mh) in mouths:
            if my < h / 2:
                continue  # Ignore false positives from nose area

            lips_roi = frame[y+my:y+my+mh, x+mx:x+mx+mw]
            gray_lips = cv2.cvtColor(lips_roi, cv2.COLOR_BGR2GRAY)
            laplacian = cv2.Laplacian(gray_lips, cv2.CV_64F)
            texture_score = np.mean(np.abs(laplacian))
            normalized_texture = min(1.0, texture_score / TEXTURE_THRESHOLD)

            if normalized_texture > DRYNESS_THRESHOLD:
                label = "Dry Lips"
                color = (0, 0, 255)  # Red
            else:
                label = "Normal Lips"
                color = (0, 255, 0)  # Green

            dryness_label = label
            cv2.rectangle(frame, (x+mx, y+my), (x+mx+mw, y+my+mh), color, 2)
            cv2.putText(frame, label, (x+mx, y+my-10),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, color, 2)

    return frame, dryness_label

# -------------------------------
# Process Frames for Lip Dryness Detection
# -------------------------------
data = b""

try:
    while True:
        # Receive frame size
        while len(data) < struct.calcsize("Q"):
            packet = client_socket.recv(4 * 1024)
            if not packet:
                break
            data += packet

        packed_msg_size = data[:struct.calcsize("Q")]
        data = data[struct.calcsize("Q"):]
        msg_size = struct.unpack("Q", packed_msg_size)[0]

        while len(data) < msg_size:
            data += client_socket.recv(4 * 1024)

        frame_data = data[:msg_size]
        data = data[msg_size:]

        # Deserialize the frame
        frame = pickle.loads(frame_data)

        # 1) Detect lip dryness
        frame, lip_status = detect_lip_dryness(frame)

        # Store lip dryness status for batch saving
        current_time = time.time()
        if lip_status and (current_time - last_saved_time >= save_interval):
            lip_dryness_batch.append(lip_status)
            last_saved_time = current_time

        # Save the batch to the database every 30 seconds
        if current_time - last_batch_time >= batch_interval:
            if lip_dryness_batch:  # Ensure there's data to save
                update_hydration_outputs(progress_report_id, lip_dryness_batch)
                lip_dryness_batch = []  # Clear batch after saving
            last_batch_time = current_time

        # Display frame
        cv2.imshow("Lip Dryness Detection", frame)
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

finally:
    client_socket.close()
    cv2.destroyAllWindows()
