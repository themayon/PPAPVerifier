import tempfile
import unittest
from pathlib import Path

from extraction_tasks import ArtifactNotFoundError, ExtractionTaskStore


class ExtractionTaskStoreTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.store = ExtractionTaskStore(self.temp_dir.name)

    def tearDown(self):
        self.temp_dir.cleanup()

    def create_task(self):
        return self.store.create_task(
            original_filename="sample.pdf",
            document_type="PFMEA",
            generate_excel=True,
            provider="gemini",
            model="test-model",
        )

    def test_task_lifecycle_and_json_artifact_are_durable(self):
        task_id = self.create_task()
        self.assertEqual(self.store.get_task(task_id)["status"], "queued")
        self.store.set_status(task_id, "running")
        self.store.write_json_artifact(task_id, "result.json", {"Is_Valid": "Yes"})
        self.store.set_status(task_id, "succeeded")

        reopened = ExtractionTaskStore(self.temp_dir.name)
        task = reopened.get_task(task_id)
        self.assertEqual(task["status"], "succeeded")
        self.assertIsNotNone(task["started_at"])
        self.assertIsNotNone(task["completed_at"])
        self.assertEqual(task["artifacts"][0]["name"], "result.json")
        artifact = reopened.get_artifact(task_id, "result.json")
        self.assertTrue(artifact["path"].is_file())

    def test_recovery_marks_in_process_work_failed(self):
        queued_id = self.create_task()
        running_id = self.create_task()
        self.store.set_status(running_id, "running")
        self.assertEqual(self.store.recover_interrupted_tasks(), 2)
        for task_id in (queued_id, running_id):
            task = self.store.get_task(task_id)
            self.assertEqual(task["status"], "failed")
            self.assertEqual(task["error_code"], "server_restarted")

    def test_artifact_name_traversal_is_rejected(self):
        task_id = self.create_task()
        source = Path(self.temp_dir.name) / "source.json"
        source.write_text("{}", encoding="utf-8")
        with self.assertRaises(ValueError):
            self.store.add_artifact(task_id, "../result.json", source, "application/json")
        with self.assertRaises(ArtifactNotFoundError):
            self.store.get_artifact(task_id, "../result.json")


if __name__ == "__main__":
    unittest.main()
