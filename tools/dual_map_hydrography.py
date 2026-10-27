"""Load the cached HydroRIVERS geography used by both map builders."""

import argparse
from dual_map_hydrosheds import acquire, load


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--map',choices=['japan','hre','both'],default='both')
    args=parser.parse_args()
    for key in ('japan','hre') if args.map=='both' else (args.map,):acquire(key)
