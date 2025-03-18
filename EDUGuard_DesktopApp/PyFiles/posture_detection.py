import cv2
import mediapipe as mp
import numpy as np
import joblib
import socket
import struct
import pickle
import time
import json
from utils.mongodb_util import update_posture_outputs
import sys

# Load the model
model_path = 'C:\\Users\\chamu\\source\\repos\\EDUGuard_DesktopApp\\EDUGuard_DesktopApp\\PyFiles\\models\\posture_classifier.pkl'
model = joblib.load(model_path)

# Get the authenticated user email from command-line arguments
if len(sys.argv) < 3:
    print("Error: Missing arguments (email, progress_report_id)")
    sys.exit(1)

USER_EMAIL = sys.argv[1]  # Get the user email from the arguments
progress_report_id = sys.argv[2]

# Connect to the webcam server
HOST = '127.0.0.1'
PORT1 = 9999

client_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
client_socket.connect((HOST, PORT1))
data = b""

# Socket to send the latest batch to C# client
PORT2 = 5001

server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)

try:
    server_socket.bind((HOST, PORT2))
    server_socket.listen(5)
    print(f"Python Server started at {HOST}:{PORT2}, waiting for connections...")

    while True:
        client_socket, addr = server_socket.accept()
        print(f"Connection established with {addr}")

        # Example data to send
        latest_posture_data = {"latest_posture": ["Good Posture", "Bad Posture", "Good Posture"]}

        try:
            json_data = json.dumps(latest_posture_data) + "\n"
            client_socket.sendall(json_data.encode("utf-8"))
            print("Sent latest posture data to C#")
        except Exception as e:
            print(f"Error sending data: {e}")
        finally:
            client_socket.close()
except Exception as e:
    print(f"Error starting server: {e}")
finally:
    server_socket.close()


# Mediapipe setup
mp_pose = mp.solutions.pose
pose = mp_pose.Pose()

# Timer setup
last_saved_time = time.time()
last_batch_time = time.time()
save_interval = 6  # Save data every 6 seconds
batch_interval = 120  # Send the latest batch to C# every 2 minutes
current_batch = []  # Temporary list to store data for the current batch

# Function to calculate angles
def calculate_angle(vector1, vector2):
    dot_product = np.dot(vector1, vector2)
    magnitude1 = np.linalg.norm(vector1)
    magnitude2 = np.linalg.norm(vector2)
    return np.degrees(np.arccos(dot_product / (magnitude1 * magnitude2)))

# Function to reconnect to C# if the connection is lost
def connect_to_csharp():
    while True:
        try:
            cs_client_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            cs_client_socket.connect((HOST, PORT2))
            print("Reconnected to C# server!")
            return cs_client_socket
        except ConnectionRefusedError:
            print("C# server is not available, retrying in 3 seconds...")
            time.sleep(3)

# Establish initial connection to C#
cs_client_socket = connect_to_csharp()

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

                    # Send the latest batch to C# application
                    json_data = json.dumps({"latest_posture": current_batch})

                    try:
                        cs_client_socket.sendall((json_data + "\n").encode("utf-8"))
                        print(f"Sent latest posture data to C#: {current_batch}")
                    except (BrokenPipeError, ConnectionResetError):
                        print("Lost connection to C# server, reconnecting...")
                        cs_client_socket = connect_to_csharp()
                        try:
                            cs_client_socket.sendall((json_data + "\n").encode("utf-8"))
                            print(f"Resent posture data to C# after reconnection: {current_batch}")
                        except Exception as e:
                            print(f"Failed to resend posture data: {e}")

                    current_batch = []  # Clear the batch after sending
                last_batch_time = current_time

        # Show the frame
        # cv2.imshow(f"{USER_EMAIL} - Posture Detection", frame)
        # if cv2.waitKey(10) & 0xFF == ord('q'):
        #     break

finally:
    client_socket.close()
    cs_client_socket.close()
    cv2.destroyAllWindows()