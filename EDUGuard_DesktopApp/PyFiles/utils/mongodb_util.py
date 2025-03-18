from pymongo import MongoClient
from bson import ObjectId
import threading
import time

# MongoDB connection setup
MONGO_URI = "mongodb+srv://myUser:myPassword123@cluster0.qk0epky.mongodb.net/?retryWrites=true&w=majority&appName=Cluster0"
client = MongoClient(MONGO_URI)
db = client["EDUGuardDB"]  
users_collection = db["Users"]  
progress_reports_collection = db["ProgressReports"]

def authenticate_user(email):
    """
    Authenticate the user by email.
    :param email: User's email address.
    :return: User document if found, otherwise None.
    """
    try:
        user = users_collection.find_one({"Email": email})
        if user:
            return user
        else:
            print(f"User with email {email} not found.")
            return None
    except Exception as e:
        print(f"Error authenticating user: {e}")
        return None

# def save_posture_data(email, posture_data):
#   """
#    Save posture data to the user's record in MongoDB.
#   :param email: User's email address.
#    :param posture_data: Posture data to save.
#    """
#   try:
#        user = authenticate_user(email)
#        if not user:
#            return
#
#        users_collection.update_one(
#            {"Email": email},
#            {"$push": {"PostureData": posture_data}}
#        )
#        print(f"Saved posture data for {email}: {posture_data}")
#    except Exception as e:
#        print(f"Error saving posture data: {e}")



def update_posture_outputs(progress_report_id, new_outputs):
    """
    Updates only the 'outputs' array in PostureData for an existing ProgressReports document.
    :param progress_report_id: The ID of the progress report to update.
    :param new_outputs: The new posture output data to be appended.
    """
    try:
        # Convert the string ID to an ObjectId
        object_id = ObjectId(progress_report_id)

        update_query = {
            "$push": {
                "PostureData.Outputs": {"$each": [new_outputs]}  
            }
        }

        result = progress_reports_collection.update_one({"_id": object_id}, update_query)

        if result.matched_count > 0:
            print(f"Successfully updated 'outputs' for Progress Report ID: {progress_report_id}")
        else:
            print(f"Progress report with ID {progress_report_id} not found.")

    except Exception as e:
        print(f"Error updating progress report: {e}")


def update_stress_outputs(progress_report_id, new_outputs):
    
    try:
        # Convert the string ID to an ObjectId
        object_id = ObjectId(progress_report_id)

        update_query = {
            "$push": {
                "StressData.Outputs": {"$each": [new_outputs]}  # Append new outputs to existing array
            }
        }

        result = progress_reports_collection.update_one({"_id": object_id}, update_query)

        if result.matched_count > 0:
            print(f"Successfully updated 'outputs' for Progress Report ID: {progress_report_id}")
        else:
            print(f"Progress report with ID {progress_report_id} not found.")

    except Exception as e:
        print(f"Error updating progress report: {e}")


def update_eye_blink_outputs(progress_report_id, batch_data):
    try:

        object_id = ObjectId(progress_report_id)

        filter_query = {"_id": object_id}
        update_query = {"$push": {
            "CVSData.Outputs": {"$each": [batch_data]}
            }
        }

        result = progress_reports_collection.update_one(filter_query, update_query)
        print(f"Updated {result.modified_count} records for eye blink data.")

    except Exception as e:
        print(f"Error updating eye blink data: {e}")


def update_hydration_outputs(progress_report_id, batch_data):
    try:

        object_id = ObjectId(progress_report_id)

        filter_query = {"_id": object_id}
        update_query = {"$push": {
            "HydrationData.Outputs": {"$each": [batch_data]}
            }
        }

        result = progress_reports_collection.update_one(filter_query, update_query)
        print(f"Updated {result.modified_count} records for hydration data.")

    except Exception as e:
        print(f"Error updating hydration data: {e}")


def refresh_progress_reports():
    """
    Refreshes the ProgressReports collection every 2 minutes.
    """
    while True:
        try:
            print("Refreshing ProgressReports collection...")

            # Fetch the latest progress report
            latest_report = progress_reports_collection.find().sort([("_id", -1)]).limit(1)
            latest_report = list(latest_report)

            if latest_report:
                print(f"Latest Progress Report ID: {latest_report[0]['_id']}")
            else:
                print("No progress reports found.")

        except Exception as e:
            print(f"Error refreshing ProgressReports: {e}")

        time.sleep(5)  # Wait for 2 minutes before refreshing again

# Start the auto-refresh in a background thread
refresh_thread = threading.Thread(target=refresh_progress_reports, daemon=True)
refresh_thread.start()
