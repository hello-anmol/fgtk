#!/usr/bin/env python
from __future__ import unicode_literals, print_function
import itertools as it, operator as op, functools as ft


import argparse
parser = argparse.ArgumentParser(
	description='Advanced/batched filesystem manipulation tool.')

# TODO: --pretend flag
parser.add_argument('--debug', action='store_true', help='Verbose operation mode.')

cmds = parser.add_subparsers(title='supported operations', help='Subcommand info.')

cmd = cmds.add_parser('mv', help='Recursively move path(s).')
cmd.add_argument('src/dst', nargs='*',
	help='Files/dirs to move. If neither of --src / --dst option is'
		' specified, last argument will be treated as destination.')
cmd.add_argument('-t', '--relocate', action='store_true',
	help='Leave symlinks in place of the original paths.')
cmd.add_argument('-r', '--relative', action='store_true',
	help='Create symlinks to relative paths, only makes sense with --relocate.')
cmd.set_defaults(call='mv')

for cmd in [op.itemgetter('mv')(cmds.choices)]:

	cmd.add_argument('-s', '--src', dest='src_opt', metavar='SRC',
		help='Source, positional argz will be treated as destination(s).')
	cmd.add_argument('-d', '--dst', dest='dst_opt', metavar='DST',
		help='Destination, positional argz will be treated as source(s).')

	cmd.add_argument('--reverse',
		action='store_true', help='Reverse source / destination targets.')

	cmd.add_argument('-P', '--attrz', action='store_true',
		help='force preserving fs metadata:'
			' uid/gid and timestamps, implied for some ops (which doesnt'
			' make sense w/o it, like cps), and conditionally implied for'
			' others (like mv), if user is root.')
	cmd.add_argument('-N', '--no-priv-attrz', action='store_true',
		help='Inhibit fs metadata copying (direct uid/gid/whatever setting will'
			' still work as requested) ops which may require elevated privileges.')

argz = parser.parse_args()
argz.pos = getattr(argz, 'src/dst')
if argz.attrz is None and argz.no_priv_attrz: argz.attrz = False


import logging
logging.basicConfig(level=logging.DEBUG if argz.debug else logging.WARNING)
log = logging.getLogger()

from tempfile import NamedTemporaryFile
from fgc import sh
from os.path import dirname, basename, abspath
import os, sys, stat


def flow_parse(argz):
	if argz.src_opt:
		ops = zip(it.repeat(argz.src_opt), (argz.pos if not argz.dst_opt else [argz.dst_opt]))
	elif argz.dst_opt: ops = zip(argz.pos, it.repeat(argz.dst_opt))
	elif len(argz.pos) < 2:
		parser.error('Need at least two positional arguments or --src / --dst specification.')
	else: ops = zip(argz.pos[:-1], it.repeat(argz.pos[-1]))
	return ops if not argz.reverse else list((dst,src) for src,dst in ops)

def flow_func(func): return lambda argz: func(argz, flow_parse(argz))


@flow_func
def mv(argz, ops):
	if not argz.relocate: mv_func = sh.mv
	else: # goal here is to create link in place of a file/dir asap
		def mv_func(src, dst, attrz):
			tmp = None
			try: os.rename(src, dst)
			except OSError:
				if not stat.S_ISDIR(os.lstat(src).st_mode):
					sh.cp_d(src, dst, attrz=attrz, dereference=False)
					os.unlink(src)
				else:
					sh.cp_r( src, dst, dereference=False,
						attrz=attrz, atom=ft.partial(sh.cp_d, skip_ts=False) )
					tmp = NamedTemporaryFile( dir=dirname(src),
						prefix='{}.'.format(basename(src)),delete=False )
					os.unlink(tmp.name), os.rename(src, tmp.name)
			sh.ln((abspath(dst) if not argz.relative else sh.relpath(dst, src)), src)
			if tmp: sh.rr(tmp.name, onerror=False)

	# attrz is implied if uid=0, otherwise disabled, unless specified explicitly in any way
	attrz = argz.attrz if argz.attrz is not None else not bool(os.getuid())

	for src,dst in ops:
		mv_func(src, sh.join(dst, basename(src)) if sh.isdir(dst) else dst, attrz=attrz)


locals()[argz.call](argz)
