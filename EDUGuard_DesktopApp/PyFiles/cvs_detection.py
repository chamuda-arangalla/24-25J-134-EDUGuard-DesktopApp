import cv2
import numpy as np
import time
from tensorflow.keras.models import load_model

# Load the pre-trained model
model_path = 'eye_blink_model.h5'
model = load_model(model_path)

# Load Haar Cascade for face detection
face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_frontalface_default.xml')

# Reference values for distance estimation
REFERENCE_FACE_WIDTH = 160  # Pixels (adjust based on testing)
NORMAL_DISTANCE_CM = 60
CLOSE_THRESHOLD = 50  # cm
FAR_THRESHOLD = 70  # cm
SCREEN_TIME_LIMIT = 20 * 60  # 20 minutes in seconds

# Function to preprocess webcam frame
def preprocess_frame(frame, target_size=(26, 34)):
    if len(frame.shape) == 3:  # Convert only if not already grayscale
        frame = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    
    resized_frame = cv2.resize(frame, (target_size[1], target_size[0]))  # Resize to (width, height)
    normalized_frame = resized_frame / 255.0  # Normalize pixel values [0, 1]
    
    return np.expand_dims(normalized_frame, axis=(0, -1))  # Add batch & channel dimensions

# Function to estimate distance from face width
def estimate_distance(face_width):
    if face_width == 0:
        return None  # No face detected
    return (REFERENCE_FACE_WIDTH * NORMAL_DISTANCE_CM) / face_width

# Initialize webcam
cap = cv2.VideoCapture(1)  # 0 for default camera

if not cap.isOpened():
    print("Error: Could not access the webcam.")
    exit()

blink_count = 0
eye_closed = False
start_time = None  # Timer for screen time tracking

while True:
    ret, frame = cap.read()
    if not ret:
        print("Error: Failed to capture frame from webcam.")
        break

    # Convert frame to grayscale for face detection
    gray_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    
    # Detect faces
    faces = face_cascade.detectMultiScale(gray_frame, scaleFactor=1.1, minNeighbors=5, minSize=(50, 50))
    
    eye_state = "Unknown"  # Default value in case no face is detected
    distance_msg = "No Face Detected"
    color = (0, 0, 255)  # Red color for warnings

    if len(faces) > 0:
        # Start or continue screen time tracking
        if start_time is None:
            start_time = time.time()
        
        x, y, w, h = max(faces, key=lambda f: f[2] * f[3])  # Select largest face
        distance_cm = estimate_distance(w)  # Estimate distance from face width

        # Distance warning messages
        if distance_cm:
            if distance_cm < CLOSE_THRESHOLD:
                distance_msg = "Too Close!"
                color = (0, 0, 255)  # Red
            elif distance_cm > FAR_THRESHOLD:
                distance_msg = "Too Far!"
                color = (255, 0, 0)  # Blue
            else:
                distance_msg = "Good Distance"
                color = (0, 255, 0)  # Green

            # Display distance info
            cv2.putText(frame, f'Distance: {int(distance_cm)} cm', (10, 90), cv2.FONT_HERSHEY_SIMPLEX, 1, color, 2)
            cv2.putText(frame, distance_msg, (10, 120), cv2.FONT_HERSHEY_SIMPLEX, 1, color, 2)

        # Draw face bounding box
        cv2.rectangle(frame, (x, y), (x + w, y + h), color, 2)

        # Define ROI for eye detection (top half of the face)
        roi = gray_frame[y:y + h // 2, x:x + w]

        # Ensure ROI has valid dimensions before processing
        if roi.size > 0 and roi.shape[0] > 0 and roi.shape[1] > 0:
            preprocessed_roi = preprocess_frame(roi)

            # Predict eye state
            prediction = model.predict(preprocessed_roi, verbose=0)
            eye_state = "Closed" if prediction > 0.5 else "Open"

            # Blink detection logic
            if eye_state == "Closed" and not eye_closed:
                eye_closed = True
            elif eye_state == "Open" and eye_closed:
                blink_count += 1
                eye_closed = False

        # Calculate time spent looking at the screen
        elapsed_time = time.time() - start_time
        minutes = int(elapsed_time // 60)
        seconds = int(elapsed_time % 60)

        # Display screen time
        cv2.putText(frame, f'Screen Time: {minutes:02}:{seconds:02}', (10, 150), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 255), 2)

        # Check if the user has been looking for more than 20 minutes
        if elapsed_time >= SCREEN_TIME_LIMIT:
            cv2.putText(frame, "Take a Break!", (100, 250), cv2.FONT_HERSHEY_SIMPLEX, 2, (0, 0, 255), 5)

    else:
        # Reset screen time tracking if no face detected
        start_time = None

    # Display blink count & eye state
    cv2.putText(frame, f'Blink Count: {blink_count}', (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
    cv2.putText(frame, f'Eye State: {eye_state}', (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)

    # Show video feed with annotations
    cv2.imshow('Eye Blink Detection & Distance Monitoring', frame)

    # Press 'q' to quit
    if cv2.waitKey(1) & 0xFF == ord('q'):
        break

# Release resources
cap.release()
cv2.destroyAllWindows()
