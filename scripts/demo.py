#!/usr/bin/env python3
"""Exercise HTTPS, Identity cookies, CSRF and catalog policies without exposing secrets."""
from __future__ import annotations

import argparse
import getpass
from http.cookiejar import CookieJar
import json
import secrets
import ssl
import sys
import urllib.error
import urllib.parse
import urllib.request


class ApiClient:
    def __init__(self, base_url: str, ca_certificate: str | None = None):
        parsed = urllib.parse.urlsplit(base_url)
        if parsed.scheme != 'https' or not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment:
            raise ValueError('Use una URL HTTPS sin credenciales, consulta ni fragmento.')
        self.base_url = base_url.rstrip('/')
        context = ssl.create_default_context(cafile=ca_certificate)
        self.cookies = CookieJar()
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPSHandler(context=context),
            urllib.request.HTTPCookieProcessor(self.cookies),
        )
        self.csrf_token: str | None = None

    def request(self, method: str, path: str, payload=None):
        headers = {'Accept': 'application/json'}
        data = None if payload is None else json.dumps(payload).encode('utf-8')
        if data is not None:
            headers['Content-Type'] = 'application/json'
        if method not in ('GET', 'HEAD') and self.csrf_token:
            headers['X-CSRF-TOKEN'] = self.csrf_token
        request = urllib.request.Request(self.base_url + path, data=data, headers=headers, method=method)
        with self.opener.open(request, timeout=15) as response:
            body = response.read()
            return None if not body else json.loads(body)

    def refresh_csrf(self):
        self.csrf_token = self.request('GET', '/api/auth/csrf')['token']

    def login(self, user_name: str, password: str):
        self.refresh_csrf()
        profile = self.request('POST', '/api/auth/login', {'userName': user_name, 'password': password})
        self.refresh_csrf()
        return profile

    def logout(self):
        self.request('POST', '/api/auth/logout', {})
        self.csrf_token = None


def catalog_demo(client: ApiClient):
    category = client.request('POST', '/api/categories', {'name': 'Demo-' + secrets.token_hex(4)})
    gadget = client.request('POST', '/api/gadgets', {
        'name': 'Teclado', 'description': 'USB', 'price': 1499.90, 'categoryId': category['id'],
    })
    saved = client.request('GET', f"/api/gadgets/{gadget['id']}")
    if saved['price'] != 1499.90 or saved['version'] != 1:
        raise ValueError('El producto leído no conserva el precio o la versión esperados.')
    updated = client.request('PUT', f"/api/gadgets/{gadget['id']}", {
        'name': 'Teclado', 'description': 'USB', 'price': 1399.90,
        'categoryId': category['id'], 'version': saved['version'],
    })
    if updated['price'] != 1399.90 or updated['version'] != 2:
        raise ValueError('La edición no devolvió el precio o la versión esperados.')
    client.request('DELETE', f"/api/gadgets/{gadget['id']}?version={updated['version']}")
    print(f"CRUD verificado; categoría {category['id']}; producto creado, consultado, editado y eliminado.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-url', default='https://localhost:7080')
    parser.add_argument('--user', required=True)
    parser.add_argument('--ca-certificate', help='Certificado PEM de confianza para el servidor local.')
    parser.add_argument('--read-only', action='store_true', help='Consultar el catálogo sin modificarlo.')
    parser.add_argument('--create-user', metavar='NAME', help='Crear una cuenta con la sesión administradora.')
    parser.add_argument('--role', choices=('Reader', 'Editor', 'Admin'), default='Reader')
    args = parser.parse_args()
    client = ApiClient(args.base_url, args.ca_certificate)
    profile = client.login(args.user, getpass.getpass('Contraseña: '))
    print(f"Sesión: {profile['userName']} ({', '.join(profile['roles'])})")
    try:
        if args.create_user:
            password = getpass.getpass('Contraseña de la nueva cuenta: ')
            if password != getpass.getpass('Confirmar contraseña: '):
                raise ValueError('Las contraseñas no coinciden.')
            user = client.request('POST', '/api/users', {'userName': args.create_user, 'password': password, 'role': args.role})
            print(f"Cuenta creada: {user['userName']} ({', '.join(user['roles'])})")
        elif args.read_only:
            categories = client.request('GET', '/api/categories')
            gadgets = client.request('GET', '/api/gadgets')
            print(f'Primera página: {len(categories)} categorías; {len(gadgets)} productos.')
        else:
            catalog_demo(client)
    finally:
        client.logout()
    print('Sesiones cerradas.')
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except urllib.error.HTTPError as error:
        print(f'La API devolvió HTTP {error.code}. Revise las credenciales, los permisos o el límite de solicitudes.', file=sys.stderr)
        sys.exit(1)
    except (urllib.error.URLError, OSError, ValueError) as error:
        print(f'No se completó la demostración: {error}', file=sys.stderr)
        sys.exit(1)
