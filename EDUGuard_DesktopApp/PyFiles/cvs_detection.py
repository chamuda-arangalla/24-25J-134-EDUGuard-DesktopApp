import cv2
import numpy as np
import time
import sys
import json
import socket
import struct
import pickle
from tensorflow.keras.models import load_model
from utils.mongodb_util import update_eye_blink_outputs  # Update function in MongoDB

# Load the pre-trained model
model_path = "C:\\Users\\chamu\\source\\repos\\EDUGuard_DesktopApp\\EDUGuard_DesktopApp\\PyFiles\\models\\eye_blink_model.h5"
model = load_model(model_path)

# Get authenticated user email & progress report ID
if len(sys.argv) < 3:
    print("Error: Missing arguments (email, progress_report_id)")
    sys.exit(1)

USER_EMAIL = sys.argv[1]
progress_report_id = sys.argv[2]

# Connect to webcam server
HOST = '127.0.0.1'
PORT = 9999

client_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
client_socket.connect((HOST, PORT))
data = b""

# Load Haar Cascade for face detection
face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_frontalface_default.xml')

# Reference values for distance estimation
REFERENCE_FACE_WIDTH = 160  # Pixels (adjust based on testing)
NORMAL_DISTANCE_CM = 60
CLOSE_THRESHOLD = 50  # cm
FAR_THRESHOLD = 70  # cm
SCREEN_TIME_LIMIT = 20 * 60  # 20 minutes
FACE_LOSS_RESET_TIME = 3  # Time in seconds to reset blink count if face is lost
EYE_LOSS_RESET_TIME = 2  # Time in seconds to reset blink count if eyes are lost

# Timer setup
last_saved_time = time.time()
last_batch_time = time.time()
save_interval = 60  # Save every 6 seconds
batch_interval = 60  # Save batch every 30 seconds
current_batch = []  # Store batch data

blink_count = 0
eye_closed = False
start_time = None  # Timer for screen time tracking
last_seen_time = time.time()  # Track last detected face time
last_eye_seen_time = time.time()  # Track last detected eye time

# Function to preprocess frame for model
def preprocess_frame(frame, target_size=(26, 34)):
    if len(frame.shape) == 3:
        frame = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    
    resized_frame = cv2.resize(frame, (target_size[1], target_size[0]))  # (width, height)
    normalized_frame = resized_frame / 255.0
    
    return np.expand_dims(normalized_frame, axis=(0, -1))

# Function to estimate distance from face width
def estimate_distance(face_width):
    if face_width == 0:
        return None  # No face detected
    return (REFERENCE_FACE_WIDTH * NORMAL_DISTANCE_CM) / face_width

try:
    while True:
        # Receive frame size
        while len(data) < struct.calcsize("Q"):
            packet = client_socket.recv(4 * 1024)
            if not packet:
                break
            data += packet

        packed_msg_size = data[:struct.calcsize("Q")]
        data = data[struct.calcsize("Q" ):]
        msg_size = struct.unpack("Q", packed_msg_size)[0]

        while len(data) < msg_size:
            data += client_socket.recv(4 * 1024)

        frame_data = data[:msg_size] 
        data = data[msg_size:]

        # Deserialize the frame
        frame = pickle.loads(frame_data)

        # Convert frame to grayscale for face detection
        gray_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        
        # Detect faces
        faces = face_cascade.detectMultiScale(gray_frame, scaleFactor=1.1, minNeighbors=5, minSize=(50, 50))
        
        eye_state = "Unknown"  # Default value
        distance_msg = "No Face Detected"
        color = (0, 0, 255)  # Red for warnings

        if len(faces) > 0:
            last_seen_time = time.time()  # Update last seen time
            
            if start_time is None:
                start_time = time.time()
            
            x, y, w, h = max(faces, key=lambda f: f[2] * f[3])
            distance_cm = estimate_distance(w)

            if distance_cm:
                if distance_cm < CLOSE_THRESHOLD:
                    distance_msg = "Too Close!"
                    color = (0, 0, 255)
                elif distance_cm > FAR_THRESHOLD:
                    distance_msg = "Too Far!"
                    color = (255, 0, 0)
                else:
                    distance_msg = "Good Distance"
                    color = (0, 255, 0)

                # Display distance info
                cv2.putText(frame, f'Distance: {int(distance_cm)} cm', (10, 90), cv2.FONT_HERSHEY_SIMPLEX, 1, color, 2)
                cv2.putText(frame, distance_msg, (10, 120), cv2.FONT_HERSHEY_SIMPLEX, 1, color, 2)

            # Draw face bounding box
            cv2.rectangle(frame, (x, y), (x + w, y + h), color, 2)

            roi = gray_frame[y:y + h // 2, x:x + w]

            if roi.size > 0:
                preprocessed_roi = preprocess_frame(roi)
                prediction = model.predict(preprocessed_roi, verbose=0)
                eye_state = "Closed" if prediction > 0.5 else "Open"
                last_eye_seen_time = time.time()

                if eye_state == "Closed" and not eye_closed:
                    eye_closed = True
                elif eye_state == "Open" and eye_closed:
                    blink_count += 1
                    eye_closed = False

            elapsed_time = time.time() - start_time

        else:
            if time.time() - last_seen_time > FACE_LOSS_RESET_TIME:
                blink_count = 0
                start_time = None

        if time.time() - last_eye_seen_time > EYE_LOSS_RESET_TIME:
            blink_count = 0

        current_time = time.time()
        if current_time - last_saved_time >= save_interval:
            data_object = {
                "eye_state": eye_state,
                "distance": distance_msg,
                "blink_count": int(blink_count)
            }
            data_string = json.dumps(data_object)
            current_batch.append(data_string)
            last_saved_time = current_time

        if current_time - last_batch_time >= batch_interval:
            if current_batch:
                update_eye_blink_outputs(progress_report_id, current_batch)
                current_batch = []
            last_batch_time = current_time

        
         # Display blink count & eye state
        cv2.putText(frame, f'Blink Count: {blink_count}', (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
        cv2.putText(frame, f'Eye State: {eye_state}', (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
        
         # Show video feed with annotations
        cv2.imshow('Eye Blink Detection & Distance Monitoring', frame)

        # Press 'q' to quit
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

finally:
    client_socket.close()
    cv2.destroyAllWindows()
