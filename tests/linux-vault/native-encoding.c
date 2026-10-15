/* SPDX-License-Identifier: LGPL-2.1-or-later
 * Compiles the exact patched upstream session source against its native static
 * dependencies. Fixed public vectors contain no runtime keys or personal data. */
#include "libsecret/secret-session.c"
#include <string.h>

int main (void)
{
    egg_libgcrypt_initialize ();
    const guchar hkdf_rfc5869_case3[16] = {0x8d,0xa4,0xe7,0x75,0xa5,0x63,0xc1,0x8f,0x71,0x5f,0x80,0x2a,0x06,0x3c,0x5a,0x31};
    const guchar gnome_reference[16] = {0x37,0x2c,0x2f,0x11,0x00,0x04,0x50,0x4e,0x29,0xf9,0x71,0xf5,0x41,0x81,0xaa,0x24};
    guchar padded[128] = {0};
    memset (padded + 106, 0x0b, 22);
    GBytes *ikm = g_bytes_new (padded, sizeof padded);
    SecretSession *kde = g_new0 (SecretSession, 1);
    SecretSession *gnome = g_new0 (SecretSession, 1);
    kde->codex_kwallet = TRUE;
    g_assert_true (codex_derive_session_key (kde, ikm));
    g_assert_cmpmem (kde->key, 16, hkdf_rfc5869_case3, 16);
    g_assert_true (codex_derive_session_key (gnome, ikm));
    g_assert_cmpmem (gnome->key, 16, gnome_reference, 16);
    g_bytes_unref (ikm); _secret_session_free (kde); _secret_session_free (gnome);
    for (guint i = 0; i < 1024; i++) {
        SecretSession *session = g_new0 (SecretSession, 1);
        session->codex_kwallet = i % 2;
        GVariant *request = g_variant_ref_sink (request_open_session_aes (session));
        GVariant *wrapped = g_variant_get_child_value (request, 1);
        GVariant *bytes = g_variant_get_variant (wrapped);
        gsize length;
        const guchar *encoded = g_variant_get_fixed_array (bytes, &length, 1);
        g_assert_nonnull (encoded);
        g_assert_cmpuint (length, ==, session->codex_kwallet ? 129 : 128);
        if (session->codex_kwallet) g_assert_cmpuint (encoded[0], ==, 0);
        g_variant_unref (bytes); g_variant_unref (wrapped); g_variant_unref (request);
        _secret_session_free (session);
    }
    g_print ("Native encoding: RFC5869 KDE leading-zero vector, unchanged GNOME derivation, 1024 unsigned/sign-safe public keys passed.\n");
    return 0;
}
