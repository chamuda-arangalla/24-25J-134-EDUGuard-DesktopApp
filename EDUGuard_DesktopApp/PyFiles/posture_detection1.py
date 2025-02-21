import cv2
import mediapipe as mp
import numpy as np
import joblib
import socket
import struct
import pickle
import time
from utils.mongodb_util import update_posture_outputs
import sys


# Load the model
model = joblib.load(r"C:\Users\chamu\source\repos\EDUGuard_DesktopApp\EDUGuard_DesktopApp\PyFiles\models\random_forest_model.joblib")

# Get the authenticated user email from command-line arguments
if len(sys.argv) < 2:
    print("Error: No email provided as an argument.")
    sys.exit(1)

USER_EMAIL = sys.argv[1]  # Get the user email from the arguments
progress_report_id = sys.argv[2]

# Connect to the webcam server
HOST = '127.0.0.1'
PORT = 9999

client_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
client_socket.connect((HOST, PORT))
data = b""

# Mediapipe setup
mp_pose = mp.solutions.pose
pose = mp_pose.Pose()

# Timer setup
last_saved_time = time.time()
last_batch_time = time.time()
save_interval = 6  # Save data every 6 seconds
batch_interval = 30  # Save the batch to the database every 30 seconds
current_batch = []  # Temporary list to store data for the current batch

# Function to calculate angles
def calculate_angle(vector1, vector2):
    dot_product = np.dot(vector1, vector2)
    magnitude1 = np.linalg.norm(vector1)
    magnitude2 = np.linalg.norm(vector2)
    return np.degrees(np.arccos(dot_product / (magnitude1 * magnitude2)))

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

        # Process frame with Mediapipe
        image_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        results = pose.process(image_rgb)

        if results.pose_landmarks:
            # Extract keypoints and calculate angles
            landmarks = results.pose_landmarks.landmark
            left_shoulder = (landmarks[mp_pose.PoseLandmark.LEFT_SHOULDER.value].x,
                             landmarks[mp_pose.PoseLandmark.LEFT_SHOULDER.value].y)
            right_shoulder = (landmarks[mp_pose.PoseLandmark.RIGHT_SHOULDER.value].x,
                              landmarks[mp_pose.PoseLandmark.RIGHT_SHOULDER.value].y)
            nose = (landmarks[mp_pose.PoseLandmark.NOSE.value].x,
                    landmarks[mp_pose.PoseLandmark.NOSE.value].y)

            h, w, _ = frame.shape
            left_shoulder = (int(left_shoulder[0] * w), int(left_shoulder[1] * h))
            right_shoulder = (int(right_shoulder[0] * w), int(right_shoulder[1] * h))
            nose = (int(nose[0] * w), int(nose[1] * h))

            green_line = np.array(right_shoulder) - np.array(left_shoulder)
            red_line = np.array([1, 0])
            blue_line = np.array(nose) - np.array([(left_shoulder[0] + right_shoulder[0]) / 2,
                                                   (left_shoulder[1] + right_shoulder[1]) / 2])

            # Calculate angles
            angle_red_green = calculate_angle(red_line, green_line)
            angle_blue_green = calculate_angle(blue_line, green_line)

            # Predict posture
            features = np.array([[angle_red_green, angle_blue_green]])
            prediction = model.predict(features)
            posture = "Good Posture" if prediction[0] == 1 else "Bad Posture"

            # Display results
            cv2.putText(frame, f"Posture: {posture}", (50, 50),
                        cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2, cv2.LINE_AA)

            # Save posture data at intervals
            current_time = time.time()
            if current_time - last_saved_time >= save_interval:
                current_batch.append(posture)
                last_saved_time = current_time

            # Save the batch to the database every 30 seconds
            if current_time - last_batch_time >= batch_interval:
                if current_batch:  # Ensure there's data to save
                    update_posture_outputs(progress_report_id, current_batch)
                    current_batch = []  # Clear the batch after saving 
                last_batch_time = current_time

        # Show the frame
        #cv2.imshow(f"{USER_EMAIL} - Posture Detection", frame)
        #if cv2.waitKey(10) & 0xFF == ord('q'):
        #   break
finally:
    client_socket.close()
    cv2.destroyAllWindows()
