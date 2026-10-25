import sys
from pathlib import Path
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from publish_release import warn_obsolete_clients


class ReleaseWarningTests(unittest.TestCase):
    def test_missing_replacement_never_changes_old_releases(self):
        api = Mock()
        with patch('publish_release.find_release', return_value=None):
            warn_obsolete_clients(api)
        api.request.assert_not_called()

    def test_only_old_clients_are_annotated_without_deleting_assets(self):
        api = Mock()
        replacement = dict(draft=False, assets=[dict(name='asn-labeler-2.1.1-setup.exe')])
        api.request.return_value = [
            dict(id=1, tag_name='client-v2.0.0', body='Original notes'),
            dict(id=2, tag_name='server-v2.0.0', body='Server'),
            dict(id=3, tag_name='client-v2.1.1', body='Current'),
            dict(id=4, tag_name='client-v1.4.3', body='<!-- unsupported-client-update-path -->'),
        ]
        with patch('publish_release.find_release', return_value=replacement):
            warn_obsolete_clients(api)
        writes = [c for c in api.request.call_args_list if c.args[0] != 'GET']
        self.assertEqual(len(writes), 1)
        self.assertEqual(writes[0].args[:2], ('PATCH', '/releases/1'))
        self.assertTrue(writes[0].args[2]['body'].endswith('Original notes'))
        self.assertIn('client-v2.1.1', writes[0].args[2]['body'])

    def test_release_without_installer_is_not_an_escape_route(self):
        api = Mock()
        with patch('publish_release.find_release', return_value=dict(draft=False, assets=[])):
            warn_obsolete_clients(api)
        api.request.assert_not_called()
