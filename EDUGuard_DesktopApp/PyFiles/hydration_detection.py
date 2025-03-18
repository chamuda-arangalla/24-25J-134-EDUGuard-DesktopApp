import cv2
import mediapipe as mp
import numpy as np
import pickle
import time
import os
import socket
import struct
import json
import sys
from datetime import datetime
from utils.mongodb_util import update_hydration_outputs  # MongoDB function

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
save_interval = 60  # Save hydration status every 1 minute
last_saved_time = time.time()
hydration_batch = []  # Store hydration data before saving

# -------------------------------
# Function to Detect Lip Dryness
# -------------------------------
def detect_lip_dryness(frame):
    """
    Detect lips in the given frame and classify as 'Dry Lips' or 'Normal Lips'.
    """
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    faces = face_cascade.detectMultiScale(gray, scaleFactor=1.3, minNeighbors=5, minSize=(60, 60))
    dryness_label = "Unknown"  # Default if no face/mouth detected

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
                dryness_label = "Dry Lips"
                color = (0, 0, 255)  # Red
            else:
                dryness_label = "Normal Lips"
                color = (0, 255, 0)  # Green

            cv2.rectangle(frame, (x+mx, y+my), (x+mx+mw, y+my+mh), color, 2)
            cv2.putText(frame, dryness_label, (x+mx, y+my-10),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, color, 2)
    return frame, dryness_label

# -------------------------------
# Water Drinking Detection Class
# -------------------------------
class WaterDrinkingDetector:
    def __init__(self):
        self.mp_pose = mp.solutions.pose
        self.pose = self.mp_pose.Pose(min_detection_confidence=0.5,
                                      min_tracking_confidence=0.5)
        self.mp_draw = mp.solutions.drawing_utils
        self.consecutive_frames = 0
        self.required_frames = 5

        self.model_file = 'C:\\Users\\chamu\\source\\repos\\EDUGuard_DesktopApp\\EDUGuard_DesktopApp\\PyFiles\\models\\drinking_model.pkl'
        self.model = None
        self.load_model()

        self.object_detector = None
        self.CLASSES = ["background", "bottle"]
        self.load_object_detector()

    def load_model(self):
        if os.path.exists(self.model_file):
            with open(self.model_file, 'rb') as f:
                self.model = pickle.load(f)
            print("Loaded drinking detection model.")
        else:
            print("No trained model found. Make sure 'drinking_model.pkl' is available.")

    def load_object_detector(self):
        prototxt = "C:\\Users\\chamu\\source\\repos\\EDUGuard_DesktopApp\\EDUGuard_DesktopApp\\PyFiles\\models\\MobileNetSSD_deploy.prototxt"
        model = "C:\\Users\\chamu\\source\\repos\\EDUGuard_DesktopApp\\EDUGuard_DesktopApp\\PyFiles\\models\\MobileNetSSD_deploy.caffemodel"
        if os.path.exists(prototxt) and os.path.exists(model):
            self.object_detector = cv2.dnn.readNetFromCaffe(prototxt, model)
            print("Loaded object detector.")
        else:
            print("Object detector files not found.")

    def detect_water_bottle(self, image):
        if self.object_detector is None:
            return False  # Return False instead of None
        (h, w) = image.shape[:2]
        blob = cv2.dnn.blobFromImage(image, 0.007843, (300, 300),
                                      (127.5, 127.5, 127.5), False)
        self.object_detector.setInput(blob)
        detections = self.object_detector.forward()

        for i in range(detections.shape[2]):
            confidence = detections[0, 0, i, 2]
            if confidence > 0.15:
                idx = int(detections[0, 0, i, 1])

                # **Fix IndexError**
                if 0 <= idx < len(self.CLASSES) and self.CLASSES[idx] == "bottle":
                    return True  # Bottle detected

        return False

# -------------------------------
# Process Frames for Hydration Detection
# -------------------------------
data = b""
hydration_detector = WaterDrinkingDetector()

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

        # 2) Detect water bottle
        bottle_detected = hydration_detector.detect_water_bottle(frame)
        hydration_status = "Hydrated" if bottle_detected else "Not Hydrated"

        # Store hydration status for batch saving
        current_time = time.time()
        if current_time - last_saved_time >= save_interval:
            data_object = {
                "lip_status": lip_status,
                "hydration_status": hydration_status
            }
            data_string = json.dumps(data_object)
            hydration_batch.append(data_string)
            last_saved_time = current_time

            # Save to database
            if hydration_batch:
                update_hydration_outputs(progress_report_id, hydration_batch)
                hydration_batch = []  # Clear batch after saving

finally:
    client_socket.close()
    cv2.destroyAllWindows()
