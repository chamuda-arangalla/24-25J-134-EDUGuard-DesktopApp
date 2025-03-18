import cv2
import numpy as np
import time
import socket
import struct
import pickle
import sys
from keras.models import load_model
from utils.mongodb_util import update_stress_outputs  

# Load the model
model = load_model(r'C:\Users\chamu\source\repos\EDUGuard_DesktopApp\EDUGuard_DesktopApp\PyFiles\models\model_file_30epochs.h5')

# Get the authenticated user email from command-line arguments
if len(sys.argv) < 2:
    print("Error: No email provided as an argument.")
    sys.exit(1)

USER_EMAIL = sys.argv[1]  # Get user email from the arguments
progress_report_id = sys.argv[2]

# Load face detector
faceDetect = cv2.CascadeClassifier(r'C:\Users\chamu\source\repos\EDUGuard_DesktopApp\EDUGuard_DesktopApp\PyFiles\utils\haarcascade_frontalface_default.xml')

# Define labels for stress detection
labels_dict = {0: 'Angry', 1: 'Disgust', 2: 'Fear', 3: 'Happy', 4: 'Neutral', 5: 'Sad', 6: 'Surprise'}

# Connect to the webcam server
HOST = '127.0.0.1'
PORT = 9999

client_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
client_socket.connect((HOST, PORT))
data = b""

# Timer setup
last_saved_time = time.time()
last_batch_time = time.time()
save_interval = 1  # Save data every 6 seconds
batch_interval = 60  # Save the batch to the database every 30 seconds
current_batch = []  # Temporary list to store data for the current batch

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

        # Convert to grayscale
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        faces = faceDetect.detectMultiScale(gray, 1.3, 3)

        for x, y, w, h in faces:
            sub_face_img = gray[y:y+h, x:x+w]
            resized = cv2.resize(sub_face_img, (48, 48))
            normalized = resized / 255.0
            reshaped = np.reshape(normalized, (1, 48, 48, 1))

            # Predict emotion
            result = model.predict(reshaped)
            label = np.argmax(result, axis=1)[0]
            detected_emotion = labels_dict[label]

            # Draw the detected face and label on the frame
            cv2.rectangle(frame, (x, y), (x + w, y + h), (0, 0, 255), 1)
            cv2.rectangle(frame, (x, y - 40), (x + w, y), (50, 50, 255), -1)
            cv2.putText(frame, detected_emotion, (x, y - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)

            # Save stress data at intervals
            current_time = time.time()
            if current_time - last_saved_time >= save_interval:
                current_batch.append(detected_emotion)  # Store in the batch
                last_saved_time = current_time

            # Save the batch to the database every 30 seconds
            if current_time - last_batch_time >= batch_interval:
                if current_batch:  # Ensure there's data to save
                    update_stress_outputs(progress_report_id, current_batch)
                    current_batch = []  # Clear batch after saving
                last_batch_time = current_time

        # Show the frame
        cv2.imshow(f"{USER_EMAIL} - Stress Detection", frame)
        if cv2.waitKey(10) & 0xFF == ord('q'):
           break
finally:
    client_socket.close()
    cv2.destroyAllWindows()
